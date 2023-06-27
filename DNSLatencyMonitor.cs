using DNS.Protocol;
using System.Net.Sockets;
using System.Net;
using Microsoft.ApplicationInsights;
using DNS.Protocol.ResourceRecords;
using Serilog;

namespace DNSMonitor
{
	internal class DNSLatencyMonitor
	{
		private readonly TelemetryClient? _tc;
		private readonly ILogger _logger;
		private readonly IPEndPoint[] _endpoints;
		private readonly Request[] _requests;
		private readonly UdpClient _udpClient;
		private readonly Dictionary<int, (DateTimeOffset RequestTime, Request Domain, IPEndPoint Resolver)> _requestLatency = new();
		private readonly int _secondsBetweenTests;
		private readonly int _secondsBeforeTimeout;

		public DNSLatencyMonitor(Config config, TelemetryClient? tc, ILogger logger)
        {
			_tc = tc;
			_logger = logger;
			_endpoints = config.Nameservers.Select(x => new IPEndPoint(IPAddress.Parse(x), 53)).ToArray();
			_requests = config.Domains.Select(x =>
			{
				var request = new Request();
				request.Questions.Add(new Question(new Domain(x)));
				request.RecursionDesired = true;
				return request;
			}).ToArray();
			_udpClient = new UdpClient();
			_secondsBetweenTests = config.SecondsBetweenTests;
			_secondsBeforeTimeout = config.SecondsBeforeTimeout;
		}
		
		public async Task RunAsync(CancellationToken cancellationToken)
			=> await Task.WhenAll(SenderAsync(cancellationToken), ReceiverAsync(cancellationToken), TimeoutMonitorAsync(cancellationToken));

		private async Task SenderAsync(CancellationToken cancellationToken)
		{
			var logger = _logger.ForContext("Operation", "Sender");
			int requestId = 1000;
			while (!cancellationToken.IsCancellationRequested)
			{
				try
				{
					foreach (var request in _requests)
					{
						foreach (var endpoint in _endpoints)
						{
							request.Id = requestId++;
							if (requestId > int.MaxValue - 10) requestId = 1000;
							DateTimeOffset now = DateTimeOffset.Now;
							
							await _udpClient.SendAsync(request.ToArray(), endpoint, cancellationToken);
							if (cancellationToken.IsCancellationRequested) return;

							_requestLatency.Add(request.Id, (now, request, endpoint));
							logger.Information("#{requestId}: {requestQuestion} -> {endpoint}", request.Id, request.Questions.First().Name, endpoint);
							
							await Task.Delay(1, cancellationToken);
							if (cancellationToken.IsCancellationRequested) return;
						}
					}
				}
				catch (Exception e)
				{
					logger.Error(e, "Error while sending, {exceptionMessage}", e.Message);
					_tc?.TrackException(e);
				}
				await Task.Delay(_secondsBetweenTests * 1000, cancellationToken);
			}
		}
		private async Task ReceiverAsync(CancellationToken cancellationToken)
		{
			var logger = _logger.ForContext("Operation", "Receiver");
			// Wait until we have sent the first request (implicit bind)
			while (!_udpClient.Client.IsBound && !cancellationToken.IsCancellationRequested)
			{
				await Task.Delay(1, cancellationToken);
			}
			while (!cancellationToken.IsCancellationRequested)
			{
				try
				{
					var receiveResult = await _udpClient.ReceiveAsync(cancellationToken);
					if (cancellationToken.IsCancellationRequested) return;

					DateTimeOffset now = DateTimeOffset.Now;
					if (!_endpoints.Any(x => x.Equals(receiveResult.RemoteEndPoint)))
						continue;
					var response = Response.FromArray(receiveResult.Buffer);


					if (!_requestLatency.ContainsKey(response.Id))
					{
						logger.Warning("#{responseId}: Received unknown response from {remoteEndpoint}", response.Id, receiveResult.RemoteEndPoint);
					}
					else
					{
						double latencyMs = (now - _requestLatency[response.Id].RequestTime).TotalMilliseconds;
						string results = string.Join(", ", response.AnswerRecords.Cast<IPAddressResourceRecord>().Select(x => x.IPAddress));

						logger.Information("#{responseId}: ({latencyMs}ms) {remoteEndpoint} -> {results}", response.Id, latencyMs, receiveResult.RemoteEndPoint, results);

						_tc?.TrackMetric("DnsResolutionLatency", latencyMs, new Dictionary<string, string>(){
							{ "Resolver", $"{receiveResult.RemoteEndPoint}" },
							{ "Domain", $"{response.Questions.First().Name}" },
						});

						_requestLatency.Remove(response.Id);
					}
				}
				catch (Exception e)
				{
					logger.Error(e, "Error while receiving, {exceptionMessage}", e.Message);
					_tc?.TrackException(e);
				}
			}
		}

		private async Task TimeoutMonitorAsync(CancellationToken cancellationToken)
		{
			var logger = _logger.ForContext("Operation", "Timeout");

			int sleepTime = Math.Min(_secondsBeforeTimeout * 500, _secondsBetweenTests * 2000);
			while (!cancellationToken.IsCancellationRequested)
			{
				await Task.Delay(sleepTime, cancellationToken);
				if (cancellationToken.IsCancellationRequested) return;

				try
				{
					var keysToCheck = _requestLatency.Keys.ToArray();
					foreach (var requestId in keysToCheck)
					{
						// Be defensive just in case
						if (_requestLatency.TryGetValue(requestId, out var requestDetails))
						{
							double latencyMs = (DateTimeOffset.Now - requestDetails.RequestTime).TotalMilliseconds;
							if (latencyMs > _secondsBeforeTimeout * 1000)
							{
								_requestLatency.Remove(requestId);
								logger.Warning("#{responseId}: ({latencyMs}ms) {remoteEndpoint} -> NO RESPONSE", requestId, latencyMs, requestDetails.Resolver);
								_tc?.TrackMetric("DnsResolutionLatency", latencyMs, new Dictionary<string, string>(){
									{ "Resolver", $"{requestDetails.Resolver}" },
									{ "Domain", $"{requestDetails.Domain.Questions.First().Name}" },
									{ "Timeout", "true" }
								});
							}
						}
					}
				}
				catch (Exception e)
				{
					logger.Error(e, "Error while checking timeouts, {exceptionMessage}", e.Message);
					_tc?.TrackException(e);
				}
			}
		}
	}
}
