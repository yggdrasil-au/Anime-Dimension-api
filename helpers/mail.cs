using RestSharp;

namespace ASP.NETCoreWebApi.helpers;

public static class Mail {
    // You can set this from Program.cs at app startup
    public static string? ApiKey { get; set; }

    /// <summary>
    /// Sends an email via MailChannels API
    /// </summary>
    public static System.String SendEmail(System.String toName, System.String toEmail, System.String subject, System.String body) {
        if (string.IsNullOrEmpty(ApiKey)) {
            throw new System.Exception("MailChannels API Key is missing. Check appsettings.Secrets.json");
        }

        // 1. Setup the client
        RestClient? client = new RestClient("https://api.mailchannels.net/tx/v1/send");
        RestRequest? request = new RestRequest("", Method.Post);

        // 2. Add Headers
        request.AddHeader("X-Api-Key", ApiKey);
        request.AddHeader("Content-Type", "application/json");

        // 3. Construct the Payload object
        // using an anonymous object lets RestSharp handle the JSON serialization automatically
        var payload = new {
            personalizations = new[] { new { to = new[] { new { email = toEmail, name = toName } } } },
            from = new {
                email = "no-reply@anime-dimension.com",
                name = "Anime Dimension no-reply"
            },
            subject,
            content = new[] {
                new {
                    type = "text/plain",
                    value = body
                }
            }
        };

        // 4. Attach Payload
        request.AddJsonBody(payload);

        // 5. Send
        RestResponse response = client.Execute(request);

        // 6. Return result
        // Check if successful (2xx status code)
        if (response.IsSuccessful) {
            return "Email sent successfully.";
        } else {
            // Log the error content for debugging
            System.Console.WriteLine($"Error: {response.Content}");
            return $"Failed: {response.ErrorMessage ?? response.Content}";
        }
    }

    ///
    // params are two arrays of strings: sendTo and sendFrom, containing the name, email, and body, etc.. all data required to send an email
    ///

    /// <summary>
    /// Sends an email via MailChannels API
    /// </summary>
    /// <returns></returns>
    public static System.String SendTestEmail(System.String[] sendTo, System.String[] sendFrom) {
        // Create a RestClient and RestRequest objects
        RestClient? client = new("https://api.mailchannels.net/tx/v1/send");
        RestRequest? request = new RestRequest("", Method.Post);
        // construct the request
        if (string.IsNullOrEmpty(ApiKey)) {
            throw new System.Exception("MailChannels API Key is missing. Check appsettings.Secrets.json");
        }
        request.AddHeader("X-Api-Key", ApiKey);
        request.AddHeader("Content-Type", "application/json");
        request.AddParameter("application/json", "{\"personalizations\":[{\"to\":[{\"email\":\"recipient@yggdrasil.au\",\"name\":\"Sakura Tanaka\"}]}],\"from\":{\"email\":\"admin@anime-dimension.com\",\"name\":\"Priya Patel\"},\"subject\":\"Testing Email API\",\"content\":[{\"type\":\"text/plain\",\"value\":\"Hi Sakura. This is just a test from Priya.\"}]}", ParameterType.RequestBody);

        // Send the request
        RestResponse response = client.Execute(request);

        // output the response data
        System.Console.WriteLine(response.Content);
        return response.Content ?? "No response content";
    }
}
