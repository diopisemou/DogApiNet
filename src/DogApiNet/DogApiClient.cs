﻿using System;
using System.Collections.Specialized;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Utf8Json;

namespace DogApiNet
{
    public partial class DogApiClient : IDisposable
    {
        public static readonly string DefaultDataDogHost = "https://app.datadoghq.com";

        [ThreadStatic]
        private static StringBuilder _buffer;

        private readonly bool _leaveDispose;

        private DogApiHttpClient _httpClient;

        public DogApiClient(string apiKey, string appKey = null, string dataDogHost = null,
            DogApiHttpClient httpClient = null, bool leaveDispose = true)
        {
            ApiKey = apiKey;
            AppKey = appKey;
            DataDogHost = dataDogHost ?? DefaultDataDogHost;
            _httpClient = httpClient ?? new DogApiHttpClientImpl();
            _leaveDispose = leaveDispose;
        }

        public string ApiKey { get; }

        public string AppKey { get; }

        public string DataDogHost { get; }

        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(1);

        private async Task<T> RequestAsync<T>(HttpMethod method, string path, NameValueCollection @params,
            DogApiHttpRequestContent data, CancellationToken? cancelToken)
        {
            @params = @params == null
                ? new NameValueCollection()
                : new NameValueCollection(@params);
            @params.Add("api_key", ApiKey);
            if (AppKey != null) @params.Add("application_key", AppKey);
            var url = DataDogHost + path;

            DogApiHttpResponseContent content;
            try
            {
                content = await (cancelToken.HasValue
                    ? _httpClient.RequestAsync(method, url, null, @params, data, cancelToken.Value)
                        .ConfigureAwait(false)
                    : _httpClient.RequestAsync(method, url, null, @params, data, Timeout).ConfigureAwait(false));
            }
            catch (DogApiClientException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new DogApiClientException("http request error", ex);
            }

            if (content.RateLimit != null)
                LatestRateLimit = content.RateLimit;

            if ((int)content.StatusCode >= 200 && (int)content.StatusCode < 300)
            {
                if (typeof(T) == typeof(NoJsonResponse)) return default(T);

                T result;
                try
                {
                    result = JsonSerializer.Deserialize<T>(content.Data);
                }
                catch (Exception ex)
                {
                    throw new DogApiClientInvalidJsonException(content.Data, ex);
                }

                return result;
            }

            if (content.MediaType == "application/json" || content.MediaType == "text/json")
            {
                DogApiErrorInfo errorInfo;
                try
                {
                    errorInfo = JsonSerializer.Deserialize<DogApiErrorInfo>(content.Data);
                }
                catch (Exception ex)
                {
                    throw new DogApiClientInvalidJsonException(content.Data, ex);
                }

                throw new DogApiErrorException(content.StatusCode, errorInfo.Errors);
            }

            throw new DogApiClientHttpException(content.StatusCode);
        }

        public static (string key, string value) DeconstructTag(string tag)
        {
            if (_buffer == null || _buffer.Capacity > 256)
                _buffer = new StringBuilder(256);
            else
                _buffer.Clear();

            var i = 0;
            for (; i < tag.Length; i++)
                if (tag[i] == ':')
                {
                    i++;
                    break;
                }
                else
                {
                    _buffer.Append(tag[i]);
                }

            var key = _buffer.ToString();

            _buffer.Clear();
            for (; i < tag.Length; i++) _buffer.Append(tag[i]);
            var value = _buffer.ToString();

            return (key, value);
        }

        private class NoJsonResponse
        {
        }

        public DogApiRateLimit LatestRateLimit { get; private set; }

        #region IDisposable Support

        private bool disposedValue; // 重複する呼び出しを検出するには

        protected virtual void Dispose(bool disposing)
        {
            if (disposedValue) return;
            if (disposing)
                if (_httpClient != null && _leaveDispose)
                {
                    _httpClient.Dispose();
                    _httpClient = null;
                }

            disposedValue = true;
        }

        public void Dispose()
        {
            Dispose(true);
        }

        #endregion
    }

    public class DogApiErrorInfo
    {
        [DataMember(Name = "errors")]
        public string[] Errors { get; set; }
    }

    public class DogTemplateVariable
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "prefix")]
        public string Prefix { get; set; }

        [DataMember(Name = "default")]
        public string Default { get; set; }
    }
}