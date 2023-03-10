module GiraffeTwilioWebhooks.App

open System.Xml.Linq
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.HttpOverrides
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.DependencyInjection
open Giraffe
open Microsoft.Extensions.Options
open Twilio.AspNet.Common
open Twilio.AspNet.Core
open Twilio.TwiML

/// Writes TwiML object to the HTTP response body
let twiml (twiml : TwiML) : HttpHandler = fun (_ : HttpFunc) (ctx : HttpContext) ->
    ctx.SetContentType "application/xml"
    ctx.WriteStringAsync(twiml.ToString(SaveOptions.DisableFormatting))
    
/// Exits out of the request pipeline
let earlyReturn : HttpFunc = Some >> HttpFuncResult.FromResult

/// Validates that the HTTP request originates from Twilio, if not returns a 403 Forbidden response.
let validateTwilioRequest : HttpHandler =
    fun (next : HttpFunc) (ctx : HttpContext) ->
        let request = ctx.Request
        let options = ctx.RequestServices.GetRequiredService<IOptionsSnapshot<TwilioRequestValidationOptions>>().Value
        let authToken = options.AuthToken
        let urlOverride =
            if System.String.IsNullOrEmpty(options.BaseUrlOverride)
            then null
            else $"{options.BaseUrlOverride}{request.Path}{request.QueryString}"
        let allowLocal = options.AllowLocal
        
        if RequestValidationHelper.IsValidRequest(ctx, authToken, urlOverride, allowLocal)
        then next ctx
        else setStatusCode 403 earlyReturn ctx

/// Handles Twilio Messaging webhook requests and responds with Messaging TwiML
let messageHandler (smsRequest: SmsRequest) = MessagingResponse().Message($"Ahoy {smsRequest.From}!") |> twiml
    
/// Handles Twilio Voice webhook requests and responds with Voice TwiML
let voiceHandler = VoiceResponse().Say("Ahoy!") |> twiml

let webApp =
    validateTwilioRequest >=> choose [
        route "/message" >=> bindForm<SmsRequest>(None) messageHandler
        route "/voice"   >=> voiceHandler
    ]

let configureApp (app : IApplicationBuilder) =
    app
        // Necessary for request validation so the reverse proxy or tunnel URL is used for validation
        .UseForwardedHeaders()
        .UseGiraffe webApp

let configureServices (services : IServiceCollection) =
    services
        .Configure<ForwardedHeadersOptions>(
            fun (options: ForwardedHeadersOptions) -> options.ForwardedHeaders <- ForwardedHeaders.All
        )
        // Configures .NET configuration for Twilio request validation
        .AddTwilioRequestValidation()
        .AddGiraffe() |> ignore

[<EntryPoint>]
let main _ =
    Host.CreateDefaultBuilder()
        .ConfigureWebHostDefaults(
            fun webHostBuilder ->
                webHostBuilder
                    .Configure(configureApp)
                    .ConfigureServices(configureServices)
                    |> ignore)
        .Build()
        .Run()
    0