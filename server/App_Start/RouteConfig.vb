Imports System.Web.Routing
Imports Microsoft.AspNet.FriendlyUrls

Public Module RouteConfig
    Sub RegisterRoutes(ByVal routes As RouteCollection)
        routes.Add("FixhandsAdminApi", New Route("api/admin/{action}", New AdminApiRouteHandler()))
        routes.Add("FixhandsProfessionalRegistration", New Route("api/professionals/register", New ProfessionalRegistrationRouteHandler()))
        routes.Add("FixhandsServiceRequests", New Route("api/requests", New ServiceRequestRouteHandler()))
        Dim settings As FriendlyUrlSettings = New FriendlyUrlSettings() With {
            .AutoRedirectMode = RedirectMode.Permanent
        }
        routes.EnableFriendlyUrls(settings)
    End Sub
End Module
