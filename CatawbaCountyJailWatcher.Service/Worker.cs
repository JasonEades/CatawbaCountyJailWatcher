using HtmlAgilityPack;
using System.Net.Mail;
using System.Net;
using System.Text;
using Azure.Communication.Email;
using Azure;
using Microsoft.Extensions.Caching.Memory;

namespace CatawbaCountyJailWatcher.Service
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private IConfiguration _configuration;
        private IMemoryCache _memoryCache;

        public Worker(ILogger<Worker> logger, IConfiguration configuration, IMemoryCache memoryCache)
        {
            _logger = logger;
            _configuration = configuration;
            _memoryCache = memoryCache;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Catawba County Jail Watcher Service Starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                var inmates = new List<Inmate>();

                _logger.LogInformation("Request running at: {time}", DateTimeOffset.Now);

                var url = "https://injail.catawbacountync.gov/WhosInJail/";
                var web = new HtmlWeb();
                var doc = await web.LoadFromWebAsync(url);

                var gv = doc.GetElementbyId("MainContent_GridView1");
                var rows = gv.SelectNodes("tr");

                var notHeader = rows.Skip(1);
                // skip header
                foreach (var row in notHeader)
                {
                    try
                    {
                        var tds = row.SelectNodes("td");
                        var name = tds[1].InnerHtml;

                        if (string.IsNullOrEmpty(name))
                        {
                            // additional offense rows
                            var inneroffense = tds[5].InnerHtml;
                            inmates.Last().Crime += $"<br> {inneroffense}";
                            continue;
                        }
                        var picLink = tds[0].InnerHtml;
                        var picStr = picLink.Split("window.open('")[1].Split("'")[0];
                        var dateConfined = tds[2].InnerHtml;
                        var address = tds[3].InnerHtml;
                        var age = tds[4].InnerHtml;
                        var offense = tds[5].InnerHtml;
                        var bond = tds[6].InnerHtml;
                        var courtDate = tds[7].InnerHtml;

                        DateTime? DateConfined = null;
                        DateTime? CourtDate = null;

                        try
                        {
                            DateConfined = DateTime.Parse(dateConfined);
                            CourtDate = DateTime.Parse(courtDate);
                        }
                        catch { }

                        inmates.Add(new Inmate()
                        {
                            PictureId = picStr,
                            Name = name,
                            Address = address,
                            Age = Convert.ToInt32(age),
                            Crime = offense,
                            DateConfined = DateConfined,
                            CourtDate = CourtDate,
                            Bond = bond
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error parsing html line: {row.InnerHtml} with error: {ex.Message}");
                        continue;
                    }

                }

                List<Inmate> namePerps = new List<Inmate>();
                List<Inmate> streetPerps = new List<Inmate>();

                var inmateNameWatchList = _configuration.GetSection("WatchNames").Get<string[]>().ToList();
                var inmateStreetWatchList = _configuration.GetSection("WatchStreets").Get<string[]>().ToList();

                foreach (var name in inmateNameWatchList)
                {
                    var found = inmates.Where(a => a.Name.Contains(name, StringComparison.InvariantCultureIgnoreCase)).ToList();

                    if (found == null || found.Count() == 0)
                        continue;


                    foreach (var f in found)
                    {
                        _logger.LogInformation($"Found perp: {f.Name} with name containing: {name}");
                        if (!namePerps.Contains(f))
                            namePerps.Add(f);
                    }

                }

                foreach (var street in inmateStreetWatchList)
                {
                    var found = inmates.Where(a => a.Address.Contains(street, StringComparison.InvariantCultureIgnoreCase)).ToList();

                    if (found == null || found.Count() == 0)
                        continue;

                    streetPerps.AddRange(found);
                }

                // determine who we have already sent an email about recently
                _memoryCache.TryGetValue("recently_sent_name_email_perps", out List<Inmate> namePerpsCache);
                _memoryCache.TryGetValue("recently_sent_street_email_perps", out List<Inmate> streetPerpsCache);
                
                if (namePerpsCache == null)
                    namePerpsCache = new List<Inmate>();

                var newEmailNamePerps = namePerps.Where(a => !namePerpsCache.Contains(a)).ToList();

                if (streetPerpsCache == null)
                    streetPerpsCache = new List<Inmate>();
                var newEmailStreetPerps = streetPerps.Where(a => !streetPerpsCache.Contains(a)).ToList();



                if (newEmailNamePerps.Count > 0 || newEmailStreetPerps.Count > 0)
                {
                    StringBuilder email = new StringBuilder();
                    string message = $"Scanning for perps with names containing: {string.Join(", ", inmateNameWatchList)}.";
                    email.Append("<h2>" + message + "</h2>");
                    email.Append("<br>");


                    if (newEmailNamePerps != null && newEmailNamePerps.Count() > 0)
                    {
                        email.Append("<table cellpadding=\"0\" cellspacing=\"0\" border=\"1\"> ");
                        email.Append("<thead>");
                        email.Append("<tr>");
                        email.Append("<td>Picture</td>");
                        email.Append("<td>Name</td>");
                        email.Append("<td>Address</td>");
                        email.Append("<td>Crime</td>");
                        email.Append("</tr>");
                        email.Append("</thead>");


                        email.Append("<tbody>");
                        foreach (var namePerp in newEmailNamePerps)
                        {
                            email.Append("<tr>");
                            email.Append($"<td><a href='{namePerp.ImageUrl}'>Mugshot</a></td>");
                            email.Append($"<td>{namePerp.Name}</td>");
                            email.Append($"<td>{namePerp.Address}</td>");
                            email.Append($"<td>{namePerp.Crime}</td>");
                            email.Append("</tr>");

                        }
                        email.Append("</tbody>");
                        email.Append("</table>");
                    }
                    else
                    {
                        email.Append("None");
                    }

                    string message2 = $"Scanning for perps with address containing: {string.Join(", ", inmateStreetWatchList)}.";
                    email.Append("<h2>" + message2 + "</h2>");
                    email.Append("<br>");


                    if (newEmailStreetPerps != null && newEmailStreetPerps.Count() > 0)
                    {
                        email.Append("<table cellpadding=\"0\" cellspacing=\"0\" border=\".5\">");
                        email.Append("<thead>");
                        email.Append("<tr>");
                        email.Append("<td>Picture</td>");
                        email.Append("<td>Name</td>");
                        email.Append("<td>Address</td>");
                        email.Append("<td>Crime</td>");
                        email.Append("</tr>");
                        email.Append("</thead>");


                        email.Append("<tbody>");
                        foreach (var namePerp in newEmailStreetPerps)
                        {
                            email.Append("<tr>");
                            email.Append($"<td><a href='{namePerp.ImageUrl}'>Mugshot</a></td>");
                            email.Append($"<td>{namePerp.Name}</td>");
                            email.Append($"<td>{namePerp.Address}</td>");
                            email.Append($"<td>{namePerp.Crime}</td>");
                            email.Append("</tr>");

                        }
                        email.Append("</tbody>");
                        email.Append("</table>");

                    }
                    else
                    {
                        email.Append("None");
                    }

                    email.Append("<br>");
                    email.Append("<br>");
                    email.Append("<p><a href='https://injail.catawbacountync.gov/WhosInJail/'>Who's in Jail?</a></p>");

                    var connectionString = _configuration.GetValue<string>("AzureCommunicationServiceConnectionString");
                    EmailClient emailClient = new EmailClient(connectionString);

                    foreach (var e in _configuration.GetValue<string>("EmailTo").Split(";"))
                    {
                        var em = new EmailMessage(_configuration.GetValue<string>("EmailFrom"), e, new EmailContent("Who's In Jail Alert") { Html = email.ToString() });

                        var emailSendOperation = emailClient.Send(
                            wait: WaitUntil.Completed, em
                          );


                        _logger.LogInformation($"Email Sent. Status = {emailSendOperation.Value.Status}");

                        string operationId = emailSendOperation.Id;
                        _logger.LogInformation($"Email operation id = {operationId}");
                    }

                    
                    // email has been sent, add these new perps to the cache

                    var cacheTimeSpanHours = _configuration.GetSection("CacheTimeSpanHours").Get<int>();

                    if (newEmailNamePerps?.Count > 0)
                    {
                        if (namePerpsCache == null)
                            namePerpsCache = new List<Inmate>();

                        namePerpsCache.AddRange(newEmailNamePerps);
                        _memoryCache.Set("recently_sent_name_email_perps", namePerpsCache, TimeSpan.FromHours(cacheTimeSpanHours));
                    }

                    if (newEmailStreetPerps?.Count > 0)
                    {
                        if (streetPerpsCache == null)
                            streetPerpsCache = new List<Inmate>();

                        streetPerpsCache.AddRange(newEmailStreetPerps);
                        _memoryCache.Set("recently_sent_street_email_perps", streetPerpsCache, TimeSpan.FromHours(cacheTimeSpanHours));
                    }

                }
                else
                {
                    _logger.LogInformation("No perps found matching criteria.");
                }

                int runIntervalHours = _configuration.GetSection("RunIntervalHours").Get<int>();
                await Task.Delay(runIntervalHours * 3600000, stoppingToken);
            }



        }

    }
}