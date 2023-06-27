# DNSMonitor

This is a small example on how to monitor DNS response latency directly. This sample is provided as-is as a reference.

In order to run, you will need to provide a `config.json` file. (_Todo: Add EnvVar support_)
 - `nameservers` should be set to the DNS servers you would like to query, IPv4 expected values
 - `domains` should contain the queries to issue to the nameservers. If there are 2 nameservers and 2 domains set, a total of 4 requests will be sent each loop (2x2)
 - `secondsBetweenTests` limits how oftern the query should happen
 - `secondsBeforeTimeout` sets how long to wait before we classify a request as timed-out. Timed out requests still log as warnings & emit to Application Insights if configured.
 - `appInsightsConnectionString` set to the Application Insights connection string you would like to report metrics to, helpful to report & view trends.
 - `fileLogFolder` path to log files to, helpful if DNS issues are also taking out the connection to application insights. Logs will roll over onto new files daily.