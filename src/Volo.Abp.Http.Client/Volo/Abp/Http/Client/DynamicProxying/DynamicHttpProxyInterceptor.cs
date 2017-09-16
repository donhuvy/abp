﻿using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.DynamicProxy;
using Volo.Abp.Http.Modeling;
using Volo.Abp.Json;
using Volo.Abp.Threading;

namespace Volo.Abp.Http.Client.DynamicProxying
{
    //TODO: Somehow capture cancellationtoken and pass to other methods...?

    public class DynamicHttpProxyInterceptor<TService> : AbpInterceptor, ITransientDependency
    {
        private static MethodInfo GenericInterceptAsyncMethod { get; }

        private readonly IDynamicProxyHttpClientFactory _httpClientFactory;
        private readonly IApiDescriptionFinder _apiDescriptionFinder;
        private readonly AbpHttpClientOptions _options;
        private readonly IJsonSerializer _jsonSerializer;

        static DynamicHttpProxyInterceptor()
        {
            GenericInterceptAsyncMethod = typeof(DynamicHttpProxyInterceptor<TService>)
                .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                .First(m => m.Name == nameof(MakeRequestAndGetResultAsync) && m.IsGenericMethodDefinition);
        }

        public DynamicHttpProxyInterceptor(
            IDynamicProxyHttpClientFactory httpClientFactory,
            IOptions<AbpHttpClientOptions> options,
            IApiDescriptionFinder apiDescriptionFinder,
            IJsonSerializer jsonSerializer)
        {
            _httpClientFactory = httpClientFactory;
            _apiDescriptionFinder = apiDescriptionFinder;
            _jsonSerializer = jsonSerializer;
            _options = options.Value;
        }

        public override void Intercept(IAbpMethodInvocation invocation)
        {
            if (invocation.Method.ReturnType == typeof(void))
            {
                AsyncHelper.RunSync(() => MakeRequest(invocation));
            }
            else
            {
                invocation.ReturnValue = _jsonSerializer.Deserialize(
                    invocation.Method.ReturnType,
                    AsyncHelper.RunSync(() => MakeRequest(invocation))
                );
            }
        }

        public override Task InterceptAsync(IAbpMethodInvocation invocation)
        {
            if (invocation.Method.ReturnType.GenericTypeArguments.IsNullOrEmpty())
            {
                return MakeRequest(invocation);
            }

            invocation.ReturnValue = GenericInterceptAsyncMethod
                .MakeGenericMethod(invocation.Method.ReturnType.GenericTypeArguments[0])
                .Invoke(this, new object[] { invocation });

            return Task.CompletedTask;
        }

        private async Task<T> MakeRequestAndGetResultAsync<T>(IAbpMethodInvocation invocation)
        {
            return _jsonSerializer.Deserialize<T>(await MakeRequest(invocation));
        }

        private async Task<string> MakeRequest(IAbpMethodInvocation invocation)
        {
            using (var client = _httpClientFactory.Create())
            {
                var proxyConfig = GetProxyConfig();
                var action = await _apiDescriptionFinder.FindActionAsync(proxyConfig, invocation.Method);
                var url = proxyConfig.BaseUrl + UrlBuilder.GenerateUrlWithParameters(action, invocation.ArgumentsDictionary);

                var requestMessage = new HttpRequestMessage(action.GetHttpMethod(), url)
                {
                    Content = RequestPayloadBuilder.BuildContent(action, invocation.ArgumentsDictionary, _jsonSerializer)
                };

                AddHeaders(invocation, action, requestMessage);
                
                var response = await client.SendAsync(requestMessage);

                if (!response.IsSuccessStatusCode)
                {
                    throw new AbpException($"Remote service returns error! HttpStatusCode: {response.StatusCode}, ReasonPhrase: {response.ReasonPhrase}");
                }

                return await response.Content.ReadAsStringAsync();
            }
        }

        private static void AddHeaders(IAbpMethodInvocation invocation, ActionApiDescriptionModel action, HttpRequestMessage requestMessage)
        {
            foreach (var headerParameter in action.Parameters.Where(p => p.BindingSourceId == "Header"))
            {
                var value = HttpActionParameterHelper.FindParameterValue(invocation.ArgumentsDictionary, headerParameter);
                if (value != null)
                {
                    requestMessage.Headers.Add(headerParameter.Name, value.ToString());
                }
            }
        }

        private DynamicHttpClientProxyConfig GetProxyConfig()
        {
            return _options.HttpClientProxies.GetOrDefault(typeof(TService))
                   ?? throw new AbpException($"Could not get DynamicHttpClientProxyConfig for {typeof(TService).FullName}.");
        }
    }
}