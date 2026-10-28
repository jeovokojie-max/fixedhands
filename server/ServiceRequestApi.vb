Imports System.Collections.Concurrent
Imports System.Configuration
Imports System.Data
Imports System.Data.SqlClient
Imports System.Globalization
Imports System.IO
Imports System.Web
Imports System.Web.Script.Serialization
Imports System.Web.Routing

Public Class ServiceRequestRouteHandler
    Implements IRouteHandler

    Public Function GetHttpHandler(requestContext As RequestContext) As IHttpHandler Implements IRouteHandler.GetHttpHandler
        Return New ServiceRequestHandler()
    End Function
End Class

Public Class ServiceRequestHandler
    Implements IHttpHandler

    Private Shared ReadOnly Serializer As New JavaScriptSerializer()
    Private Shared ReadOnly Attempts As New ConcurrentDictionary(Of String, RequestAttempt)(StringComparer.Ordinal)

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
        If context.Request.HttpMethod <> "POST" Then
            WriteError(context, 405, "Use POST to submit a service request.")
            Return
        End If
        If Not AllowRequest(context) Then
            WriteError(context, 429, "Too many requests. Please wait a few minutes and try again.")
            Return
        End If

        Try
            Dim input = ReadRequest(context)
            If input Is Nothing OrElse Not IsValid(input) Then
                WriteError(context, 400, "Complete your name, phone, service, area, job address, and availability.")
                Return
            End If

            Dim connectionString = Environment.GetEnvironmentVariable("FIXHANDS_SQL_CONNECTION")
            If String.IsNullOrWhiteSpace(connectionString) Then
                Dim setting = ConfigurationManager.ConnectionStrings("Fixhands")
                If setting IsNot Nothing Then connectionString = setting.ConnectionString
            End If
            If String.IsNullOrWhiteSpace(connectionString) Then
                WriteError(context, 503, "Service requests are not connected to the Fixhands database yet.")
                Return
            End If

            Dim requestId As Long
            Dim submittedAt = DateTime.UtcNow
            Dim professionalId As Guid
            Dim hasProfessional = Not String.IsNullOrWhiteSpace(input.ProfessionalId)
            If hasProfessional AndAlso Not Guid.TryParse(input.ProfessionalId, professionalId) Then
                WriteError(context, 400, "The selected professional is invalid. Please choose a professional again.")
                Return
            End If
            Using connection As New SqlConnection(connectionString)
                connection.Open()
                Using transaction = connection.BeginTransaction()
                Using command As New SqlCommand("INSERT INTO dbo.ServiceRequests (CustomerName, PhoneNumber, Service, Area, JobAddress, Availability, Details, SubmittedAtUtc) VALUES (@CustomerName, @PhoneNumber, @Service, @Area, @JobAddress, @Availability, @Details, @SubmittedAtUtc); SELECT CONVERT(bigint, SCOPE_IDENTITY());", connection, transaction)
                    command.Parameters.Add("@CustomerName", SqlDbType.NVarChar, 100).Value = input.Name.Trim()
                    command.Parameters.Add("@PhoneNumber", SqlDbType.NVarChar, 50).Value = input.Phone.Trim()
                    command.Parameters.Add("@Service", SqlDbType.NVarChar, 100).Value = input.Service.Trim()
                    command.Parameters.Add("@Area", SqlDbType.NVarChar, 200).Value = input.Location.Trim()
                    command.Parameters.Add("@JobAddress", SqlDbType.NVarChar, 300).Value = input.Address.Trim()
                    command.Parameters.Add("@Availability", SqlDbType.NVarChar, 100).Value = input.Availability.Trim()
                    command.Parameters.Add("@Details", SqlDbType.NVarChar, 1000).Value = If(input.Details Is Nothing, "", input.Details.Trim())
                    command.Parameters.Add("@SubmittedAtUtc", SqlDbType.DateTime2).Value = submittedAt
                    requestId = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture)
                End Using
                If hasProfessional Then
                    Using command As New SqlCommand("INSERT INTO dbo.ServiceRequestAssignments (RequestId, ProfessionalId) SELECT @RequestId, ProfessionalId FROM dbo.Skilledreg WHERE ProfessionalId = @ProfessionalId AND Services = @Service AND Area = @Area; IF @@ROWCOUNT = 0 THROW 50011, 'Selected professional is unavailable for this service or area.', 1;", connection, transaction)
                        command.Parameters.Add("@RequestId", SqlDbType.BigInt).Value = requestId
                        command.Parameters.Add("@ProfessionalId", SqlDbType.UniqueIdentifier).Value = professionalId
                        command.Parameters.Add("@Service", SqlDbType.NVarChar, 100).Value = input.Service.Trim()
                        command.Parameters.Add("@Area", SqlDbType.NVarChar, 200).Value = input.Location.Trim()
                        command.ExecuteNonQuery()
                    End Using
                End If
                transaction.Commit()
                End Using
            End Using

            WriteJson(context, 201, New With {.requestId = requestId, .created = submittedAt.ToString("o", CultureInfo.InvariantCulture), .assigned = hasProfessional})
        Catch ex As SqlException
            context.Trace.Warn("Fixhands.ServiceRequests", "Could not save a service request.", ex)
            If ex.Number = 50011 Then
                WriteError(context, 400, "The selected professional does not match this service and area. Choose the professional again and submit the request.")
                Return
            End If
            If ex.Number = 4060 OrElse ex.Number = 916 Then
                WriteError(context, 503, "Fixhands could not open its database. Check the server-side connection setting and database access.")
                Return
            End If
            WriteError(context, 503, "We could not save your request right now. Please try again later.")
        Catch ex As Exception
            context.Trace.Warn("Fixhands.ServiceRequests", "Service request submission failed.", ex)
            WriteError(context, 500, "We could not save your request right now. Please try again later.")
        End Try
    End Sub

    Private Shared Function ReadRequest(context As HttpContext) As ServiceRequestInput
        Using reader As New StreamReader(context.Request.InputStream, context.Request.ContentEncoding)
            Dim body = reader.ReadToEnd()
            If String.IsNullOrWhiteSpace(body) Then Return Nothing
            Return Serializer.Deserialize(Of ServiceRequestInput)(body)
        End Using
    End Function

    Private Shared Function IsValid(input As ServiceRequestInput) As Boolean
        Return Not String.IsNullOrWhiteSpace(input.Name) AndAlso input.Name.Length <= 100 AndAlso
            Not String.IsNullOrWhiteSpace(input.Phone) AndAlso input.Phone.Length <= 50 AndAlso
            Not String.IsNullOrWhiteSpace(input.Service) AndAlso input.Service.Length <= 100 AndAlso
            Not String.IsNullOrWhiteSpace(input.Location) AndAlso input.Location.Length <= 200 AndAlso
            Not String.IsNullOrWhiteSpace(input.Address) AndAlso input.Address.Length <= 300 AndAlso
            Not String.IsNullOrWhiteSpace(input.Availability) AndAlso input.Availability.Length <= 100 AndAlso
            (input.Details Is Nothing OrElse input.Details.Length <= 1000)
    End Function

    Private Shared Function AllowRequest(context As HttpContext) As Boolean
        Dim key = If(context.Request.UserHostAddress, "unknown")
        Dim now = DateTimeOffset.UtcNow
        Dim attempt = Attempts.AddOrUpdate(key,
            Function(ignored) New RequestAttempt(1, now),
            Function(ignored, current)
                If now - current.WindowStart >= TimeSpan.FromMinutes(5) Then Return New RequestAttempt(1, now)
                Return New RequestAttempt(current.Count + 1, current.WindowStart)
            End Function)
        Return attempt.Count <= 20
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
            context.Response.Headers("Access-Control-Allow-Methods") = "POST, OPTIONS"
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

    Private Class ServiceRequestInput
        Public Property Name As String
        Public Property Phone As String
        Public Property Service As String
        Public Property Location As String
        Public Property Address As String
        Public Property Availability As String
        Public Property Details As String
        Public Property ProfessionalId As String
    End Class

    Private Structure RequestAttempt
        Public Sub New(countValue As Integer, windowStartValue As DateTimeOffset)
            Count = countValue
            WindowStart = windowStartValue
        End Sub
        Public Count As Integer
        Public WindowStart As DateTimeOffset
    End Structure
End Class
