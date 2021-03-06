﻿using System;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using NElasticsearch.Helpers;
using RestSharp;
using RestSharp.Deserializers;

namespace NElasticsearch
{
    public class ElasticsearchRestClient : ElasticsearchClient
    {
        private readonly ClientsPool<RestClient> _clientsPool = new ClientsPool<RestClient>();

        public ElasticsearchRestClient(params string[] elasticsearchUrls)
            : this(elasticsearchUrls.Select(x => new Uri(x)).ToArray())
        {  
        }

        public ElasticsearchRestClient(params Uri[] elasticsearchUrls)
        {
            MaxConcurrentConnections = 10;

            foreach (var elasticsearchUrl in elasticsearchUrls)
            {
                for (var i = 0; i < MaxConcurrentConnections; i++)
                {
                    var internalClient = new RestClient(elasticsearchUrl.ToString())
                    {
                        Authenticator = Authenticator,
                        CookieContainer = CookieContainer,
                        Proxy = Proxy,
                        UserAgent = UserAgent,
                        UseSynchronizationContext = UseSynchronizationContext,
                        Timeout = Timeout,
                        ClientCertificates = ClientCertificates,
                    };
                    internalClient.ClearHandlers();
                    internalClient.AddHandler("application/json", new JsonDeserializer());
                    internalClient.AddHandler("text/json", new JsonDeserializer());
                    internalClient.AddHandler("text/x-json", new JsonDeserializer());
                    internalClient.AddHandler("*", new JsonDeserializer());
                    _clientsPool.Add(internalClient);
                }
            }            
        }

        private static Method TranslateRestMethod(RestMethod method)
        {
            switch (method)
            {
                case RestMethod.POST:
                    return Method.POST;
                case RestMethod.PUT:
                    return Method.PUT;
                case RestMethod.DELETE:
                    return Method.DELETE;
                case RestMethod.GET:
                    return Method.GET;
                case RestMethod.HEAD:
                    return Method.HEAD;
                case RestMethod.OPTIONS:
                    return Method.OPTIONS;
                default:
                    throw new Exception("Unrecognized REST method");
            }
        }

        public override async Task<T> Execute<T>(RestMethod method, string url, object body = null)
        {
            var request = new RestRequest(url, TranslateRestMethod(method)) { RequestFormat = DataFormat.Json };

            var bodyStr = body as string;
            if (bodyStr != null)
            {
                request.AddParameter("text/json", body, ParameterType.RequestBody);
            }
            else if (body != null)
            {
                request.AddBody(body);
            }

            var response = await Execute<T>(request);

            if (response.ErrorException != null || response.StatusCode == 0)
                throw new Exception("NElasticsearch internal error", response.ErrorException);

            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new ResourceNotFoundException(url);

            if (response.StatusCode != HttpStatusCode.OK
                && response.StatusCode != HttpStatusCode.Accepted
                && response.StatusCode != HttpStatusCode.Created)
                throw ElasticsearchException.CreateFromResponseBody(response.Content);

            return response.Data;
        }

        public override async Task<string> Execute(RestMethod method, string url, object body = null)
        {
            var request = new RestRequest(url, TranslateRestMethod(method)) { RequestFormat = DataFormat.Json };

            var bodyStr = body as string;
            if (bodyStr != null)
            {
                request.AddParameter("text/json", body, ParameterType.RequestBody);
            }
            else if (body != null)
            {
                request.AddBody(body);
            }

            var response = await Execute(request);

            if (response.ErrorException != null || response.StatusCode == 0)
                throw new Exception("NElasticsearch internal error", response.ErrorException);

            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new ResourceNotFoundException(url);

            if (response.StatusCode != HttpStatusCode.OK
                && response.StatusCode != HttpStatusCode.Accepted
                && response.StatusCode != HttpStatusCode.Created)
                throw ElasticsearchException.CreateFromResponseBody(response.Content);

            return response.Content;
        }

        private async Task<IRestResponse> Execute(IRestRequest request)
        {
            while (true)
            {
                var endpoint = _clientsPool.GetEndpoint();
                using (new DisposableAction(() => _clientsPool.ReleaseEndpoint(endpoint)))
                {
                    // TODO propogate exceptions properly
                    var rsp = await endpoint.RestClient.ExecuteTaskAsync(request);
                    if (rsp.StatusCode == 0 && rsp.ErrorException is WebException)
                    {
                        endpoint.MarkFailure();
                        continue;
                    }
                    return rsp;
                }
            }
        }

        private async Task<IRestResponse<T>> Execute<T>(IRestRequest request) where T : new()
        {
            while (true)
            {                
                var endpoint = _clientsPool.GetEndpoint();
                using (new DisposableAction(() => _clientsPool.ReleaseEndpoint(endpoint)))
                {                   
                    var rsp = await endpoint.RestClient.ExecuteTaskAsync<T>(request);
                    if (rsp.StatusCode == 0 && rsp.ErrorException is WebException)
                    {
                        endpoint.MarkFailure();
                        continue;
                    }
                    return rsp;   
                }
            }
        }

        public int MaxConcurrentConnections { get; set; }
        public CookieContainer CookieContainer { get; set; }
        public string UserAgent { get; set; }
        public int Timeout { get; set; }
        public bool UseSynchronizationContext { get; set; }
        public IAuthenticator Authenticator { get; set; }
        public X509CertificateCollection ClientCertificates { get; set; }
        public IWebProxy Proxy { get; set; }
    }
}
