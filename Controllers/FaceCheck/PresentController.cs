using System.Net;
using System.Text.Json;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using WoodgroveDemo.Helpers;
using WoodgroveDemo.Models;
using Microsoft.Extensions.Logging;
using WoodgroveDemo.Models.Presentation;
using System.Net.Http.Headers;
using System.Text;

namespace WoodgroveDemo.Controllers.FaceCheck;

[ApiController]
[Route("[controller]")]
public class PresentController : ControllerBase
{

    protected readonly IConfiguration _Configuration;
    protected TelemetryClient _Telemetry;
    protected IMemoryCache _Cache;
    protected Settings _Settings { get; set; }
    protected readonly IHttpClientFactory _HttpClientFactory;
    public ResponseToClient _Response { get; set; } = new ResponseToClient();

    public PresentController(TelemetryClient telemetry, IConfiguration configuration, IMemoryCache cache, IHttpClientFactory httpClientFactory)
    {
        _Configuration = configuration;
        _Cache = cache;
        _Telemetry = telemetry;
        _HttpClientFactory = httpClientFactory;

        // Load the settings of this demo
        _Settings = new Settings(configuration, "IdTokenHint", true);
    }

    [AllowAnonymous]
    [HttpGet("/api/FaceCheck/Present")]

    public async Task<ResponseToClient> Get()
    {
        // Send telemetry from this web app to Application Insights.
        // TBD AppInsightsHelper.TrackPage(_telemetry, this.Request);

        // Clear session
        this.HttpContext.Session.Clear();

        try
        {
            // Create a presentation request object
            PresentationRequest request = RequestHelper.CreatePresentationRequest(_Settings, this.Request, null, true);

            // Serialize the request object to JSON HTML format
            _Response.RequestPayload = request.ToHtml();

            // Prepare the HTTP request with the Bearer access token and the request body
            var client = _HttpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await MsalAccessTokenHandler.AcquireToken(_Settings, _Cache));

            // Call the Microsoft Entra ID request endpoint
            HttpResponseMessage res = await client.PostAsync(
                _Settings.RequestUrl.Replace("/v1.0/", "/beta/"),
                new StringContent(request.ToString(), Encoding.UTF8, "application/json"));

            _Response.ResponseBody = await res.Content.ReadAsStringAsync();
            HttpStatusCode statusCode = res.StatusCode;

            if (statusCode == HttpStatusCode.Created)
            {
                PresentationResponse presentationResponse = PresentationResponse.Parse(_Response.ResponseBody);
                _Response.ResponseBody = presentationResponse.ToHtml();

                _Response.QrCodeUrl = presentationResponse.url;

                // Add the state ID to the user's session object 
                this.HttpContext.Session.SetString("state", request.callback.state);

                // Add the global cache with the request status
                Status status = new Status()
                {
                    RequestStateId = request.callback.state,
                    RequestStatus = Constants.RequestStatus.REQUEST_CREATED,
                    Flow = "Presentation"
                };

                _Cache.Set(request.callback.state, status.ToString(), DateTimeOffset.Now.AddMinutes(Constants.AppSettings.CACHE_EXPIRES_IN_MINUTES));
            }
            else
            {
                _Response.ErrorMessage = _Response.ResponseBody;
                _Response.ErrorUserMessage = ResponseError.Parse(_Response.ResponseBody).GetUserMessage();
            }
        }
        catch (Exception ex)
        {
            _Response.ErrorMessage = ex.Message;
        }

        return _Response;
    }

}