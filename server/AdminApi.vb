Imports System.Collections.Concurrent
Imports System.Configuration
Imports System.Data
Imports System.Data.SqlClient
Imports System.Globalization
Imports System.Security.Cryptography
Imports System.Text
Imports System.Web
Imports System.Web.Routing
Imports System.Web.Script.Serialization

Public Class AdminApiRouteHandler
    Implements IRouteHandler

    Public Function GetHttpHandler(requestContext As RequestContext) As IHttpHandler Implements IRouteHandler.GetHttpHandler
        Return New AdminApiHandler(Convert.ToString(requestContext.RouteData.Values("action"), CultureInfo.InvariantCulture))
    End Function
End Class

Public Class AdminApiHandler
    Implements IHttpHandler

    Private Shared ReadOnly Sessions As New ConcurrentDictionary(Of String, AdminSession)(StringComparer.Ordinal)
    Private Shared ReadOnly Attempts As New ConcurrentDictionary(Of String, LoginAttempt)(StringComparer.Ordinal)
    Private Shared ReadOnly Serializer As New JavaScriptSerializer()
    Private Const SessionHours As Integer = 8
    Private Const HashIterations As Integer = 100000
    Private ReadOnly actionName As String

    Public Sub New(actionValue As String)
        actionName = If(actionValue, "").ToLowerInvariant()
    End Sub

    Public ReadOnly Property IsReusable As Boolean Implements IHttpHandler.IsReusable
        Get
            Return True
        End Get
    End Property

    Public Sub ProcessRequest(context As HttpContext) Implements IHttpHandler.ProcessRequest
        context.Response.ContentType = "application/json; charset=utf-8"
        context.Response.Cache.SetCacheability(HttpCacheability.NoCache)
        context.Response.Cache.SetNoStore()
        context.Response.TrySkipIisCustomErrors = True
        context.Response.Headers("X-Content-Type-Options") = "nosniff"
        ApplyCors(context)

        If context.Request.HttpMethod = "OPTIONS" Then
            context.Response.StatusCode = 204
            Return
        End If

        Try
            Select Case actionName
                Case "login"
                    If context.Request.HttpMethod <> "POST" Then
                        WriteError(context, 405, "Use POST for admin sign-in.")
                        Return
                    End If
                    If Not AllowLoginAttempt(context) Then
                        WriteError(context, 429, "Too many sign-in attempts. Wait a few minutes and try again.")
                        Return
                    End If
                    Login(context)
                Case "session"
                    If context.Request.HttpMethod <> "GET" Then
                        WriteError(context, 405, "Use GET to check an admin session.")
                        Return
                    End If
                    CheckSession(context)
                Case "logout"
                    If context.Request.HttpMethod <> "POST" Then
                        WriteError(context, 405, "Use POST to end an admin session.")
                        Return
                    End If
                    Logout(context)
                Case Else
                    WriteError(context, 404, "Admin endpoint not found.")
            End Select
        Catch ex As SqlException
            ' Keep SQL/server details out of client responses.
            context.Trace.Warn("Fixhands.AdminApi", "Could not query dbo.TblLogin. Verify the SQL configuration, migration, and permissions.", ex)
            WriteError(context, 503, GetSafeSqlDiagnostic(ex))
        Catch ex As Exception
            context.Trace.Warn("Fixhands.AdminApi", "Admin API request failed.", ex)
            WriteError(context, 500, "The sign-in service encountered an error. Please try again.")
        End Try
    End Sub

    Private Shared Function GetSafeSqlDiagnostic(exception As SqlException) As String
        Dim errorNumber = If(exception.Errors.Count > 0, exception.Errors(0).Number, exception.Number)
        Select Case errorNumber
            Case 18456, 18452
                Return "SQL diagnostic 18456: SQL Server rejected the IIS login. Check the server-side SQL username/password and SQL authentication mode."
            Case 4060
                Return "SQL diagnostic 4060: SQL Server could not open the fixhands database for this login. Check the database name and grant the SQL login access."
            Case 207
                Return "SQL diagnostic 207: the login table is missing a required column. Back up the database and run upgrade-admin-login.sql."
            Case 208
                Return "SQL diagnostic 208: dbo.TblLogin was not found. Check the database name and table schema."
            Case 229
                Return "SQL diagnostic 229: the SQL login lacks permission on dbo.TblLogin. Grant SELECT and UPDATE to the API login."
            Case -2, 2, 26, 40, 53, 67, 121, 233, 258, 10054, 10060, 10061, 11001
                Return "SQL diagnostic " & Convert.ToString(errorNumber, CultureInfo.InvariantCulture) & ": IIS could not reach SQL Server. Check the SQL host, TCP port, firewall, and SQL Server TCP/IP settings."
            Case Else
                Return "The login service could not reach its database (SQL diagnostic " & Convert.ToString(errorNumber, CultureInfo.InvariantCulture) & "). Check the server trace for details."
        End Select
    End Function

    Private Shared Sub ApplyCors(context As HttpContext)
        Dim origin = context.Request.Headers("Origin")
        If String.IsNullOrWhiteSpace(origin) Then Return
        Dim configured = ConfigurationManager.AppSettings("FixhandsAllowedOrigins")
        If String.IsNullOrWhiteSpace(configured) Then configured = "https://appassets.androidplatform.net"
        Dim allowed = configured.Split(";"c).Select(Function(value) value.Trim()).Where(Function(value) value.Length > 0)
        If allowed.Any(Function(value) String.Equals(value, origin, StringComparison.OrdinalIgnoreCase)) Then
            context.Response.Headers("Access-Control-Allow-Origin") = origin
            context.Response.Headers("Vary") = "Origin"
            context.Response.Headers("Access-Control-Allow-Methods") = "GET, POST, OPTIONS"
            context.Response.Headers("Access-Control-Allow-Headers") = "Content-Type, Authorization"
            context.Response.Headers("Access-Control-Max-Age") = "600"
        End If
    End Sub

    Private Shared Sub Login(context As HttpContext)
        Dim input = ReadRequest(Of LoginRequest)(context)
        Dim username = If(input Is Nothing OrElse input.Username Is Nothing, "", input.Username.Trim())
        Dim password = If(input Is Nothing, Nothing, input.Password)
        If String.IsNullOrWhiteSpace(username) OrElse String.IsNullOrEmpty(password) Then
            WriteError(context, 400, "Enter your username and password.")
            Return
        End If

        Dim connectionString = Environment.GetEnvironmentVariable("FIXHANDS_SQL_CONNECTION")
        If String.IsNullOrWhiteSpace(connectionString) Then
            Dim connectionSetting = ConfigurationManager.ConnectionStrings("Fixhands")
            If connectionSetting IsNot Nothing Then connectionString = connectionSetting.ConnectionString
        End If
        If String.IsNullOrWhiteSpace(connectionString) Then
            WriteError(context, 503, "The SQL connection is not configured on the IIS server.")
            Return
        End If

        Using connection As New SqlConnection(connectionString)
            connection.Open()
            Using command As New SqlCommand("SELECT TOP (1) [Usernames], [Password], [Fullnames], [PasswordHash], [IsAdmin] FROM Login WHERE [Usernames] = @username;", connection)
                command.Parameters.Add("@username", SqlDbType.NVarChar, 256).Value = username
                Dim storedUsername As String = Nothing
                Dim legacyPassword As String = Nothing
                Dim fullName As String = Nothing
                Dim passwordHash As String = Nothing
                Dim isAdmin = False
                Using reader = command.ExecuteReader()
                    If Not reader.Read() Then
                        WriteError(context, 401, "Sign-in failed. Check your username and password.")
                        Return
                    End If
                    storedUsername = If(reader.IsDBNull(0), Nothing, Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture))
                    legacyPassword = If(reader.IsDBNull(1), Nothing, Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture))
                    fullName = If(reader.IsDBNull(2), Nothing, Convert.ToString(reader.GetValue(2), CultureInfo.InvariantCulture))
                    passwordHash = If(reader.IsDBNull(3), Nothing, Convert.ToString(reader.GetValue(3), CultureInfo.InvariantCulture))
                    isAdmin = Not reader.IsDBNull(4) AndAlso Convert.ToBoolean(reader.GetValue(4), CultureInfo.InvariantCulture)
                End Using

                If Not isAdmin OrElse String.IsNullOrWhiteSpace(storedUsername) Then
                    WriteError(context, 401, "Sign-in failed. Check your username and password.")
                    Return
                End If

                Dim needsUpgrade = False
                If Not String.IsNullOrWhiteSpace(passwordHash) Then
                    If Not VerifyPassword(password, passwordHash, needsUpgrade) Then
                        WriteError(context, 401, "Sign-in failed. Check your username and password.")
                        Return
                    End If
                Else
                    ' A legacy password can be used once to migrate it into the ASP.NET Identity hash format.
                    If Not String.Equals(legacyPassword, password, StringComparison.Ordinal) Then
                        WriteError(context, 401, "Sign-in failed. Check your username and password.")
                        Return
                    End If
                    needsUpgrade = True
                End If

                If needsUpgrade OrElse (Not String.IsNullOrWhiteSpace(legacyPassword) AndAlso legacyPassword <> "!") Then
                    Dim newHash = If(needsUpgrade, CreateIdentityHash(password), passwordHash)
                    Using upgrade As New SqlCommand("UPDATE Login SET [PasswordHash] = @passwordHash, [Password] = N'!' WHERE [Usernames] = @username;", connection)
                        upgrade.Parameters.Add("@passwordHash", SqlDbType.NVarChar, 512).Value = newHash
                        upgrade.Parameters.Add("@username", SqlDbType.NVarChar, 256).Value = storedUsername
                        upgrade.ExecuteNonQuery()
                    End Using
                End If

                Dim tokenBytes(31) As Byte
                Using rng = RandomNumberGenerator.Create()
                    rng.GetBytes(tokenBytes)
                End Using
                Dim token = Convert.ToBase64String(tokenBytes).TrimEnd("="c).Replace("+", "-").Replace("/", "_")
                Dim expiresAt = DateTimeOffset.UtcNow.AddHours(SessionHours)
                Sessions(token) = New AdminSession(storedUsername, If(fullName, storedUsername), expiresAt)
                Dim expirationText As String = String.Format(CultureInfo.InvariantCulture, "{0:o}", expiresAt.UtcDateTime)
                WriteJson(context, 200, New With {.accessToken = token, .fullName = If(fullName, storedUsername), .expiresAt = expirationText})
            End Using
        End Using
    End Sub

    Private Shared Sub CheckSession(context As HttpContext)
        Dim token = ReadBearerToken(context.Request)
        Dim session As AdminSession = Nothing
        If token IsNot Nothing AndAlso Sessions.TryGetValue(token, session) AndAlso session.ExpiresAt > DateTimeOffset.UtcNow Then
            Dim expirationText As String = String.Format(CultureInfo.InvariantCulture, "{0:o}", session.ExpiresAt.UtcDateTime)
            WriteJson(context, 200, New With {.username = session.Username, .fullName = session.FullName, .expiresAt = expirationText})
            Return
        End If
        If token IsNot Nothing Then
            Dim removed As AdminSession = Nothing
            Sessions.TryRemove(token, removed)
        End If
        WriteError(context, 401, "Admin session expired. Sign in again.")
    End Sub

    Private Shared Sub Logout(context As HttpContext)
        Dim token = ReadBearerToken(context.Request)
        If token IsNot Nothing Then
            Dim removed As AdminSession = Nothing
            Sessions.TryRemove(token, removed)
        End If
        context.Response.StatusCode = 204
    End Sub

    Private Shared Function AllowLoginAttempt(context As HttpContext) As Boolean
        Dim key = If(context.Request.UserHostAddress, "unknown")
        Dim now = DateTimeOffset.UtcNow
        Dim attempt = Attempts.AddOrUpdate(key,
            Function(ignored) New LoginAttempt(1, now),
            Function(ignored, current)
                If now - current.WindowStart >= TimeSpan.FromMinutes(5) Then Return New LoginAttempt(1, now)
                Return New LoginAttempt(current.Count + 1, current.WindowStart)
            End Function)
        Return attempt.Count <= 8
    End Function

    Private Shared Function ReadBearerToken(request As HttpRequest) As String
        Dim header = request.Headers("Authorization")
        If header IsNot Nothing AndAlso header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) Then Return header.Substring(7).Trim()
        Return Nothing
    End Function

    Private Shared Function ReadRequest(Of T)(context As HttpContext) As T
        Dim input As IO.Stream = context.Request.InputStream
        Using reader As New IO.StreamReader(input, context.Request.ContentEncoding)
                Dim body = reader.ReadToEnd()
                If String.IsNullOrWhiteSpace(body) Then Return Nothing
                Return Serializer.Deserialize(Of T)(body)
        End Using
    End Function

    Private Shared Sub WriteError(context As HttpContext, statusCode As Integer, message As String)
        WriteJson(context, statusCode, New With {.message = message})
    End Sub

    Private Shared Sub WriteJson(context As HttpContext, statusCode As Integer, value As Object)
        context.Response.StatusCode = statusCode
        context.Response.Write(Serializer.Serialize(value))
    End Sub

    Private Shared Function CreateIdentityHash(password As String) As String
        Dim salt(15) As Byte
        Using rng = RandomNumberGenerator.Create()
            rng.GetBytes(salt)
        End Using
        Dim subkey(31) As Byte
        Using derive = New Rfc2898DeriveBytes(password, salt, HashIterations, HashAlgorithmName.SHA256)
            subkey = derive.GetBytes(32)
        End Using
        Dim output(1 + 4 + 4 + 4 + salt.Length + subkey.Length - 1) As Byte
        output(0) = 1
        WriteUInt32BigEndian(output, 1, 1) ' HMAC-SHA256
        WriteUInt32BigEndian(output, 5, HashIterations)
        WriteUInt32BigEndian(output, 9, salt.Length)
        Buffer.BlockCopy(salt, 0, output, 13, salt.Length)
        Buffer.BlockCopy(subkey, 0, output, 13 + salt.Length, subkey.Length)
        Return Convert.ToBase64String(output)
    End Function

    Private Shared Function VerifyPassword(password As String, encodedHash As String, ByRef needsUpgrade As Boolean) As Boolean
        needsUpgrade = False
        Try
            Dim decoded = Convert.FromBase64String(encodedHash)
            If decoded.Length < 13 OrElse decoded(0) <> 1 Then Return False
            Dim prf = ReadUInt32BigEndian(decoded, 1)
            Dim iterations = CInt(ReadUInt32BigEndian(decoded, 5))
            Dim saltLength = CInt(ReadUInt32BigEndian(decoded, 9))
            If iterations < 1 OrElse saltLength < 1 OrElse decoded.Length < 13 + saltLength + 16 Then Return False
            Dim algorithm As HashAlgorithmName
            Select Case prf
                Case 0 : algorithm = HashAlgorithmName.SHA1
                Case 1 : algorithm = HashAlgorithmName.SHA256
                Case 2 : algorithm = HashAlgorithmName.SHA512
                Case Else : Return False
            End Select
            Dim expected(decoded.Length - 13 - saltLength - 1) As Byte
            Buffer.BlockCopy(decoded, 13 + saltLength, expected, 0, expected.Length)
            Dim salt(saltLength - 1) As Byte
            Buffer.BlockCopy(decoded, 13, salt, 0, salt.Length)
            Dim actual(expected.Length - 1) As Byte
            Using derive = New Rfc2898DeriveBytes(password, salt, iterations, algorithm)
                actual = derive.GetBytes(expected.Length)
            End Using
            Dim valid = FixedTimeEquals(actual, expected)
            needsUpgrade = valid AndAlso (iterations < HashIterations OrElse prf <> 1 OrElse saltLength <> 16 OrElse expected.Length <> 32)
            Return valid
        Catch ex As FormatException
            Return False
        Catch ex As CryptographicException
            Return False
        End Try
    End Function

    Private Shared Function FixedTimeEquals(left As Byte(), right As Byte()) As Boolean
        If left Is Nothing OrElse right Is Nothing OrElse left.Length <> right.Length Then Return False
        Dim difference As Integer = 0
        For byteIndex As Integer = 0 To left.Length - 1
            difference = difference Or (CInt(left(byteIndex)) Xor CInt(right(byteIndex)))
        Next
        Return difference = 0
    End Function

    Private Shared Sub WriteUInt32BigEndian(target As Byte(), offset As Integer, value As Integer)
        target(offset) = CByte((value >> 24) And &HFF)
        target(offset + 1) = CByte((value >> 16) And &HFF)
        target(offset + 2) = CByte((value >> 8) And &HFF)
        target(offset + 3) = CByte(value And &HFF)
    End Sub

    Private Shared Function ReadUInt32BigEndian(source As Byte(), offset As Integer) As UInteger
        Return (CUInt(source(offset)) << 24) Or (CUInt(source(offset + 1)) << 16) Or (CUInt(source(offset + 2)) << 8) Or CUInt(source(offset + 3))
    End Function

    Private Class LoginRequest
        Public Property Username As String
        Public Property Password As String
    End Class

    Private Class AdminSession
        Public Sub New(usernameValue As String, fullNameValue As String, expiresValue As DateTimeOffset)
            Username = usernameValue
            FullName = fullNameValue
            ExpiresAt = expiresValue
        End Sub
        Public Property Username As String
        Public Property FullName As String
        Public Property ExpiresAt As DateTimeOffset
    End Class

    Private Structure LoginAttempt
        Public Sub New(countValue As Integer, windowStartValue As DateTimeOffset)
            Count = countValue
            WindowStart = windowStartValue
        End Sub
        Public Count As Integer
        Public WindowStart As DateTimeOffset
    End Structure
End Class
