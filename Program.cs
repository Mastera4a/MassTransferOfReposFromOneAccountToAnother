using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

class Program
{
    private static readonly HttpClient client = new HttpClient();

    static async Task Main()
    {
        Console.Write("Введіть токен вихідного акаунту: ");
        string sourceToken = Console.ReadLine();

        Console.Write("Введіть ім'я вихідного акаунту: ");
        string sourceUsername = Console.ReadLine();

        Console.Write("Введіть токен нового акаунту: ");
        string targetToken = Console.ReadLine();

        Console.Write("Введіть ім'я нового акаунту: ");
        string targetUsername = Console.ReadLine();

        await TransferRepositories(sourceUsername, sourceToken, targetUsername, targetToken);
    }

    static async Task TransferRepositories(string sourceUsername, string sourceToken, string targetUsername, string targetToken)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sourceToken);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");

        string url = $"https://api.github.com/users/{sourceUsername}/repos";
        HttpResponseMessage response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        string responseBody = await response.Content.ReadAsStringAsync();
        JsonDocument json = JsonDocument.Parse(responseBody);

        foreach (JsonElement repo in json.RootElement.EnumerateArray())
        {
            string repoName = repo.GetProperty("name").GetString();
            bool isPrivate = repo.GetProperty("private").GetBoolean();

            Console.WriteLine($"🔄 Перенесення {repoName}...");

            bool created = await CreateRepository(targetUsername, targetToken, repoName, isPrivate);
            if (created)
            {
                await TransferRepositoryContent(sourceUsername, sourceToken, targetUsername, targetToken, repoName);
            }
        }
    }

    static async Task<bool> CreateRepository(string targetUsername, string targetToken, string repoName, bool isPrivate)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", targetToken);

        string url = "https://api.github.com/user/repos";
        string jsonBody = JsonSerializer.Serialize(new
        {
            name = repoName,
            @private = isPrivate
        });

        HttpContent content = new StringContent(jsonBody);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        HttpResponseMessage response = await client.PostAsync(url, content);
        return response.IsSuccessStatusCode;
    }

    static async Task TransferRepositoryContent(string sourceUsername, string sourceToken, string targetUsername, string targetToken, string repoName)
    {
        string sourceRepoUrl = $"https://{sourceUsername}:{sourceToken}@github.com/{sourceUsername}/{repoName}.git";
        string targetRepoUrl = $"https://{targetUsername}:{targetToken}@github.com/{targetUsername}/{repoName}.git";

        Console.WriteLine($"🔁 Клонування {repoName}...");
        await RunGitCommand($"clone --bare {sourceRepoUrl} temp_repo");

        Console.WriteLine($"🚀 Відправка {repoName} у {targetUsername}...");
        await RunGitCommand($"push --mirror {targetRepoUrl}", "temp_repo");

        Console.WriteLine($"🗑️ Видалення тимчасового репозиторію...");
        await RunGitCommand("rm -rf temp_repo");
    }

    static async Task RunGitCommand(string command, string workingDirectory = "")
    {
        var processInfo = new System.Diagnostics.ProcessStartInfo("git", command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrEmpty(workingDirectory))
        {
            processInfo.WorkingDirectory = workingDirectory;
        }

        var process = System.Diagnostics.Process.Start(processInfo);
        await process.WaitForExitAsync();
    }
}
