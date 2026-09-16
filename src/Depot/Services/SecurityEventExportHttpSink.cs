// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Depot.Models;

namespace Depot.Services;

internal sealed class SecurityEventSinkException : Exception
{
	public SecurityEventSinkException(SecurityEventDeliveryFailureKind kind, string code, string message, Exception? innerException = null)
		: base(message, innerException)
	{
		Kind = kind;
		Code = code;
	}

	public SecurityEventDeliveryFailureKind Kind { get; }
	public string Code { get; }
}

internal interface ISecurityEventExportSinkFactory : IDisposable
{
	ISecurityEventExportSink Create(SecurityEventExportTarget target);
}

internal sealed class HttpJsonSecurityEventExportSinkFactory : ISecurityEventExportSinkFactory
{
	public const string Code = "http-json-v1";
	private readonly HttpClient _httpClient;
	private readonly bool _ownsClient;

	public HttpJsonSecurityEventExportSinkFactory(HttpClient? httpClient = null)
	{
		_httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
		_ownsClient = httpClient is null;
	}

	public ISecurityEventExportSink Create(SecurityEventExportTarget target)
	{
		ArgumentNullException.ThrowIfNull(target);
		if (!string.Equals(target.SinkCode, Code, StringComparison.Ordinal))
			throw new SecurityEventSinkException(SecurityEventDeliveryFailureKind.Permanent, "UNKNOWN_SINK", $"Security-event export sink '{target.SinkCode}' is not supported.");
		if (!Uri.TryCreate(target.EndpointUri, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(endpoint.UserInfo))
			throw new SecurityEventSinkException(SecurityEventDeliveryFailureKind.Permanent, "INVALID_ENDPOINT", "Security-event HTTP export requires an absolute HTTPS endpoint without embedded credentials.");
		return new HttpJsonSecurityEventExportSink(_httpClient, target.Code, endpoint);
	}

	public void Dispose()
	{
		if (_ownsClient) _httpClient.Dispose();
	}
}

internal sealed class HttpJsonSecurityEventExportSink : ISecurityEventExportSink
{
	private readonly HttpClient _httpClient;
	private readonly string _targetCode;
	private readonly Uri _endpoint;

	public HttpJsonSecurityEventExportSink(HttpClient httpClient, string targetCode, Uri endpoint)
	{
		_httpClient = httpClient;
		_targetCode = targetCode;
		_endpoint = endpoint;
	}

	public string SinkCode => HttpJsonSecurityEventExportSinkFactory.Code;

	public async Task WriteAsync(SecurityEventExportBatch batch, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(batch);
		var payload = JsonSerializer.Serialize(new
		{
			formatVersion = batch.FormatVersion,
			targetCode = _targetCode,
			snapshotUpperBoundId = batch.SnapshotUpperBoundId,
			startCheckpoint = batch.StartCheckpoint,
			nextCheckpoint = batch.NextCheckpoint,
			events = batch.Events
		});
		using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
		{
			Content = new StringContent(payload, Encoding.UTF8, "application/json")
		};
		request.Headers.TryAddWithoutValidation("X-Depot-Delivery-Id", ComputeDeliveryId(batch));
		request.Headers.TryAddWithoutValidation("X-Depot-Export-Format", batch.FormatVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
		HttpResponseMessage response;
		try
		{
			response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
		catch (OperationCanceledException exception)
		{
			throw new SecurityEventSinkException(SecurityEventDeliveryFailureKind.Transient, "HTTP_TIMEOUT", "Security-event export request timed out.", exception);
		}
		catch (HttpRequestException exception)
		{
			throw new SecurityEventSinkException(SecurityEventDeliveryFailureKind.Transient, "HTTP_TRANSPORT", "Security-event export transport failed.", exception);
		}

		using (response)
		{
			if (response.IsSuccessStatusCode) return;
			var status = (int)response.StatusCode;
			var kind = response.StatusCode == HttpStatusCode.RequestTimeout || response.StatusCode == (HttpStatusCode)429 || status >= 500
				? SecurityEventDeliveryFailureKind.Transient
				: SecurityEventDeliveryFailureKind.Permanent;
			throw new SecurityEventSinkException(kind, $"HTTP_{status}", $"Security-event export endpoint returned HTTP {status} ({response.ReasonPhrase ?? "no reason"}).");
		}
	}

	internal string ComputeDeliveryId(SecurityEventExportBatch batch)
	{
		var material = $"v1|{_targetCode}|{batch.StartCheckpoint.FilterSha256}|{batch.StartCheckpoint.LastEventId}|{batch.NextCheckpoint.LastEventId}|{batch.SnapshotUpperBoundId}";
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
	}
}
