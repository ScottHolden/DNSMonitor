using System.Net;

namespace DNSMonitor;

record Config(
	string[] Nameservers, 
	string[] Domains, 
	int SecondsBetweenTests, 
	int SecondsBeforeTimeout,
	string? AppInsightsConnectionString, 
	string FileLogFolder)
{
	public IEnumerable<string> Validate()
	{
		if (Nameservers == null || Nameservers.Length == 0)
			yield return "At least 1 nameserver must be configured.";
		else
			foreach(var ns in Nameservers)
			{
				if (string.IsNullOrWhiteSpace(ns) || !IPAddress.TryParse(ns, out IPAddress? ip) || ip == null)
				{
					yield return $"Invalid nameserver '{ns}', expecting valid IP";
				}
			}

		if (Domains == null || Domains.Length == 0)
			yield return "At least 1 nameserver must be configured.";
		else
			foreach (var domain in Domains)
			{
				if (string.IsNullOrWhiteSpace(domain) || Uri.CheckHostName(domain) != UriHostNameType.Dns)
				{
					yield return $"Invalid domain '{domain}', expecting valid DNS name";
				}
			}

		if (SecondsBetweenTests < 0)
			yield return "SecondsBetweenTests must be 0 or higher";

		if (SecondsBeforeTimeout < 1)
			yield return "SecondsBeforeTimeout must be 1 or higher";
	}
}