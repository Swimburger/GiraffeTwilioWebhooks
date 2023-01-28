module GiraffeTwilioWebhooks.App

open System.Threading.Tasks
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

// ---------------------------------
// Web app
// ---------------------------------
    
let twiml (twiml : TwiML) : HttpHandler = fun (_ : HttpFunc) (ctx : HttpContext) ->
    task {
        ctx.SetContentType "application/xml"
        let! _ = twiml.ToXDocument()
                         .SaveAsync(ctx.Response.Body, SaveOptions.DisableFormatting, ctx.RequestAborted)
                         .ConfigureAwait(false)
        return Some ctx
    }
    
let earlyReturn : HttpFunc = Some >> HttpFuncResult.FromResult

let validateTwilioRequest : HttpHandler =
    fun (next : HttpFunc) (ctx : HttpContext) ->
        let request = ctx.Request
        let options = ctx.RequestServices.GetRequiredService<IOptionsSnapshot<TwilioRequestValidationOptions>>().Value
        let authToken = options.AuthToken
        let urlOverride =
            if System.String.IsNullOrEmpty(options.BaseUrlOverride)
            then null
            else $"{options.BaseUrlOverride}{request.Path}{request.QueryString}"
        let allowLocal = if options.AllowLocal.HasValue then options.AllowLocal.Value else true
        
        if RequestValidationHelper.IsValidRequest(ctx, authToken, urlOverride, allowLocal)
        then next ctx
        else setStatusCode 403 earlyReturn ctx
            
let messageHandler (smsRequest: SmsRequest) = MessagingResponse().Message($"Ahoy {smsRequest.From}!") |> twiml
    
let voiceHandler = VoiceResponse().Say("Ahoy!") |> twiml

let webApp =
    validateTwilioRequest >=> choose [
        route "/message" >=> bindForm<SmsRequest>(None) messageHandler
        route "/voice"   >=> voiceHandler
    ]

let configureApp (app : IApplicationBuilder) =
    // Add Giraffe to the ASP.NET Core pipeline
    app
        .UseForwardedHeaders()
        .UseGiraffe webApp

let configureServices (services : IServiceCollection) =
    // Add Giraffe dependencies
    services
        .Configure<ForwardedHeadersOptions>(
            fun (options: ForwardedHeadersOptions) -> options.ForwardedHeaders <- ForwardedHeaders.All
        )
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