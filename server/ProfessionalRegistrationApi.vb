Imports System.Collections.Concurrent
Imports System.Configuration
Imports System.Data
Imports System.Data.SqlClient
Imports System.Globalization
Imports System.IO
Imports System.Web
Imports System.Web.Script.Serialization
Imports System.Web.Routing

Public Class ProfessionalRegistrationRouteHandler
    Implements IRouteHandler

    Public Function GetHttpHandler(requestContext As RequestContext) As IHttpHandler Implements IRouteHandler.GetHttpHandler
        Return New ProfessionalRegistrationHandler()
    End Function
End Class

Public Class ProfessionalRegistrationHandler
    Implements IHttpHandler

    Private Shared ReadOnly Serializer As New JavaScriptSerializer()
    Private Shared ReadOnly Attempts As New ConcurrentDictionary(Of String, RegistrationAttempt)(StringComparer.Ordinal)

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
        ApplyCors(context)

        If context.Request.HttpMethod = "OPTIONS" Then
            context.Response.StatusCode = 204
            Return
        End If
        If context.Request.HttpMethod = "GET" Then
            WriteDirectory(context)
            Return
        End If
        If context.Request.HttpMethod <> "POST" Then
            WriteError(context, 405, "Use GET to browse professionals or POST to submit a professional profile.")
            Return
        End If
        If Not AllowRegistration(context) Then
            WriteError(context, 429, "Too many submissions. Please wait a few minutes and try again.")
            Return
        End If

        Try
            Dim input = ReadRequest(context)
            If input Is Nothing OrElse Not IsValid(input) Then
                WriteError(context, 400, "Complete your name, phone, service, area, address, and availability.")
                Return
            End If

            Dim connectionString = Environment.GetEnvironmentVariable("FIXHANDS_SQL_CONNECTION")
            If String.IsNullOrWhiteSpace(connectionString) Then
                Dim setting = ConfigurationManager.ConnectionStrings("Fixhands")
                If setting IsNot Nothing Then connectionString = setting.ConnectionString
            End If
            If String.IsNullOrWhiteSpace(connectionString) Then
                WriteError(context, 503, "Professional registration is not connected to Skilledreg yet.")
                Return
            End If

            Dim professionalId As Guid
            Using connection As New SqlConnection(connectionString)
                connection.Open()
                Using command As New SqlCommand("INSERT INTO dbo.Skilledreg (FNames, phone, Services, Area, Address, Available, Workabout) OUTPUT inserted.ProfessionalId VALUES (@FNames, @phone, @services, @Area, @Address, @Available, @Workabout);", connection)
                    command.Parameters.Add("@FNames", SqlDbType.NVarChar, 100).Value = input.Name.Trim()
                    command.Parameters.Add("@phone", SqlDbType.NVarChar, 50).Value = input.Phone.Trim()
                    command.Parameters.Add("@services", SqlDbType.NVarChar, 100).Value = input.Service.Trim()
                    command.Parameters.Add("@Area", SqlDbType.NVarChar, 200).Value = input.Location.Trim()
                    command.Parameters.Add("@Address", SqlDbType.NVarChar, 200).Value = input.Address.Trim()
                    command.Parameters.Add("@Available", SqlDbType.NVarChar, 100).Value = input.Availability.Trim()
                    command.Parameters.Add("@Workabout", SqlDbType.NVarChar, 300).Value = If(input.Bio Is Nothing, "", input.Bio.Trim())
                    professionalId = DirectCast(command.ExecuteScalar(), Guid)
                End Using
            End Using

            WriteJson(context, 201, New With {.professionalId = professionalId})
        Catch ex As SqlException
            context.Trace.Warn("Fixhands.ProfessionalRegistration", "Could not save a professional registration in Skilledreg.", ex)
            If ex.Number = 2601 OrElse ex.Number = 2627 Then
                WriteError(context, 409, "A profile already exists for this phone number.")
                Return
            End If
            If ex.Number = 4060 OrElse ex.Number = 916 Then
                WriteError(context, 503, "Fixhands could not open its fixhands database. Check the server-side connection setting and database access.")
                Return
            End If
            WriteError(context, 503, "We could not save your profile right now. Please try again later.")
        Catch ex As Exception
            context.Trace.Warn("Fixhands.ProfessionalRegistration", "Professional registration failed.", ex)
            WriteError(context, 500, "We could not save your profile right now. Please try again later.")
        End Try
    End Sub

    Private Shared Sub WriteDirectory(context As HttpContext)
        Dim connectionString = Environment.GetEnvironmentVariable("FIXHANDS_SQL_CONNECTION")
        If String.IsNullOrWhiteSpace(connectionString) Then
            Dim setting = ConfigurationManager.ConnectionStrings("Fixhands")
            If setting IsNot Nothing Then connectionString = setting.ConnectionString
        End If
        If String.IsNullOrWhiteSpace(connectionString) Then
            WriteError(context, 503, "The professional directory is not connected to the Fixhands database yet.")
            Return
        End If
        Try
            Dim rows As New List(Of Object)()
            Using connection As New SqlConnection(connectionString)
                connection.Open()
                Using command As New SqlCommand("SELECT CONVERT(nvarchar(36), ProfessionalId) AS id, FNames AS name, Services AS service, Area AS location, Available AS availability, Workabout AS bio FROM dbo.Skilledreg WHERE ProfessionalId IS NOT NULL ORDER BY FNames;", connection)
                    Using reader = command.ExecuteReader()
                        While reader.Read()
                            rows.Add(New With {.id = Convert.ToString(reader("id"), CultureInfo.InvariantCulture), .name = Convert.ToString(reader("name"), CultureInfo.InvariantCulture), .service = Convert.ToString(reader("service"), CultureInfo.InvariantCulture), .location = Convert.ToString(reader("location"), CultureInfo.InvariantCulture), .availability = Convert.ToString(reader("availability"), CultureInfo.InvariantCulture), .bio = Convert.ToString(reader("bio"), CultureInfo.InvariantCulture)})
                        End While
                    End Using
                End Using
            End Using
            WriteJson(context, 200, rows)
        Catch ex As Exception
            context.Trace.Warn("Fixhands.ProfessionalDirectory", "Could not load the public directory.", ex)
            WriteError(context, 503, "We could not load the professional directory right now.")
        End Try
    End Sub

    Private Shared Function ReadRequest(context As HttpContext) As RegistrationRequest
        Using reader As New StreamReader(context.Request.InputStream, context.Request.ContentEncoding)
            Dim body = reader.ReadToEnd()
            If String.IsNullOrWhiteSpace(body) Then Return Nothing
            Return Serializer.Deserialize(Of RegistrationRequest)(body)
        End Using
    End Function

    Private Shared Function IsValid(input As RegistrationRequest) As Boolean
        Return Not String.IsNullOrWhiteSpace(input.Name) AndAlso input.Name.Length <= 100 AndAlso
            Not String.IsNullOrWhiteSpace(input.Phone) AndAlso input.Phone.Length <= 50 AndAlso
            Not String.IsNullOrWhiteSpace(input.Service) AndAlso input.Service.Length <= 100 AndAlso
            Not String.IsNullOrWhiteSpace(input.Location) AndAlso input.Location.Length <= 200 AndAlso
            Not String.IsNullOrWhiteSpace(input.Address) AndAlso input.Address.Length <= 200 AndAlso
            Not String.IsNullOrWhiteSpace(input.Availability) AndAlso input.Availability.Length <= 100 AndAlso
            (input.Bio Is Nothing OrElse input.Bio.Length <= 300)
    End Function

    Private Shared Function AllowRegistration(context As HttpContext) As Boolean
        Dim key = If(context.Request.UserHostAddress, "unknown")
        Dim now = DateTimeOffset.UtcNow
        Dim attempt = Attempts.AddOrUpdate(key,
            Function(ignored) New RegistrationAttempt(1, now),
            Function(ignored, current)
                If now - current.WindowStart >= TimeSpan.FromMinutes(5) Then Return New RegistrationAttempt(1, now)
                Return New RegistrationAttempt(current.Count + 1, current.WindowStart)
            End Function)
        Return attempt.Count <= 10
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
            context.Response.Headers("Access-Control-Allow-Headers") = "Content-Type"
            context.Response.Headers("Access-Control-Max-Age") = "600"
        End If
    End Sub

    Private Shared Sub WriteError(context As HttpContext, statusCode As Integer, message As String)
        WriteJson(context, statusCode, New With {.message = message})
    End Sub

    Private Shared Sub WriteJson(context As HttpContext, statusCode As Integer, value As Object)
        context.Response.StatusCode = statusCode
        context.Response.Write(Serializer.Serialize(value))
    End Sub

    Private Class RegistrationRequest
        Public Property Name As String
        Public Property Phone As String
        Public Property Service As String
        Public Property Location As String
        Public Property Address As String
        Public Property Availability As String
        Public Property Bio As String
    End Class

    Private Structure RegistrationAttempt
        Public Sub New(countValue As Integer, windowStartValue As DateTimeOffset)
            Count = countValue
            WindowStart = windowStartValue
        End Sub
        Public Count As Integer
        Public WindowStart As DateTimeOffset
    End Structure
End Class
