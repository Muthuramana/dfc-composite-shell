﻿using DFC.Composite.Shell.ClientHandlers;
using DFC.Composite.Shell.Common;
using DFC.Composite.Shell.Policies.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;
using System;

namespace DFC.Composite.Shell.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddPolicies(
            this IServiceCollection services,
            IConfiguration configuration,
            string configurationSectionName = Constants.Policies)
        {
            var section = configuration.GetSection(configurationSectionName);
            services.Configure<PolicyOptions>(configuration);
            var policyOptions = section.Get<PolicyOptions>();

            var policyRegistry = services.AddPolicyRegistry();
            policyRegistry.Add(
                PolicyName.HttpRetry,
                HttpPolicyExtensions
                    .HandleTransientHttpError()
                    .WaitAndRetryAsync(
                        policyOptions.HttpRetry.Count,
                        retryAttempt => TimeSpan.FromSeconds(Math.Pow(policyOptions.HttpRetry.BackoffPower, retryAttempt))));
            policyRegistry.Add(
                PolicyName.HttpCircuitBreaker,
                HttpPolicyExtensions
                    .HandleTransientHttpError()
                    .CircuitBreakerAsync(
                        handledEventsAllowedBeforeBreaking: policyOptions.HttpCircuitBreaker.ExceptionsAllowedBeforeBreaking,
                        durationOfBreak: policyOptions.HttpCircuitBreaker.DurationOfBreak));

            return services;
        }

        public static IServiceCollection AddHttpClient<TClient, TImplementation, TClientOptions>(
            this IServiceCollection services,
            IConfiguration configuration,
            string configurationSectionName)
            where TClient : class
            where TImplementation : class, TClient
            where TClientOptions : HttpClientOptions, new() =>
            services
                .Configure<TClientOptions>(configuration.GetSection(configurationSectionName))
                .AddHttpClient<TClient, TImplementation>()
                .ConfigureHttpClient((sp, options) =>
                {
                    var httpClientOptions = sp
                        .GetRequiredService<IOptions<TClientOptions>>()
                        .Value;
                    options.BaseAddress = httpClientOptions.BaseAddress;
                    options.Timeout = httpClientOptions.Timeout;
                })
                .ConfigurePrimaryHttpMessageHandler(x => new DefaultHttpClientHandler())
                .AddPolicyHandlerFromRegistry(PolicyName.HttpRetry)
                .AddPolicyHandlerFromRegistry(PolicyName.HttpCircuitBreaker)
                .Services;
    }
}