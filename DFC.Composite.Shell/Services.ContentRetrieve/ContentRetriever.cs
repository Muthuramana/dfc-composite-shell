﻿using System;
using System.Net.Http;
using System.Threading.Tasks;
using DFC.Composite.Shell.Models;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;

namespace DFC.Composite.Shell.Services.ContentRetrieve
{
    public class ContentRetriever : IContentRetriever
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ContentRetriever> _logger;

        public ContentRetriever(HttpClient httpClient, ILogger<ContentRetriever> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<string> GetContent(string url, bool isHealthy, string offlineHtml)
        {
            string results = null;

            try
            {
                if (isHealthy)
                {
                    _logger.LogInformation($"{nameof(GetContent)}: Getting child response from: {url}");

                    var response = await _httpClient.GetAsync(url);

                    response.EnsureSuccessStatusCode();

                    results = await response.Content.ReadAsStringAsync();

                    _logger.LogInformation($"{nameof(GetContent)}: Received child response from: {url}");
                }
                else
                {
                    if (!string.IsNullOrEmpty(offlineHtml))
                    {
                        results = offlineHtml;
                    }
                }
            }
            catch (BrokenCircuitException ex)
            {
                _logger.LogError(ex, $"{nameof(ContentRetriever)}: BrokenCircuit: {url} - {ex.Message}");

                if (!string.IsNullOrEmpty(offlineHtml))
                {
                    results = offlineHtml;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"{nameof(ContentRetriever)}: {url} - {ex.Message}");

                if (!string.IsNullOrEmpty(offlineHtml))
                {
                    results = offlineHtml;
                }
            }

            return results;
        }
    }
}
