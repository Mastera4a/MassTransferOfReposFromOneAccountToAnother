using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Linq;
using System.Threading;

class Program
{
    private static readonly HttpClient client = new HttpClient();

    static async Task Main()
    {
        Console.Write("Введіть токен вихідного акаунту: ");
        string sourceToken = Console.ReadLine().Trim();

        Console.Write("Введіть ім'я вихідного акаунту: ");
        string sourceUsername = Console.ReadLine().Trim();

        Console.Write("Введіть токен нового акаунту: ");
        string targetToken = Console.ReadLine().Trim();

        Console.Write("Введіть ім'я нового акаунту: ");
        string targetUsername = Console.ReadLine().Trim();

        if (string.IsNullOrEmpty(sourceToken) || string.IsNullOrEmpty(targetToken) ||
            string.IsNullOrEmpty(sourceUsername) || string.IsNullOrEmpty(targetUsername))
        {
            Console.WriteLine("❌ Помилка: Усі поля повинні бути заповнені.");
            return;
        }

        await TransferRepositories(sourceUsername, sourceToken, targetUsername, targetToken);
    }

    static async Task TransferRepositories(string sourceUsername, string sourceToken, string targetUsername, string targetToken)
    {
        int page = 1;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sourceToken);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");

        while (true)
        {
            string url = $"https://api.github.com/users/{sourceUsername}/repos?per_page=100&page={page}";
            HttpResponseMessage response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"❌ Помилка отримання репозиторіїв: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
                return;
            }

            string responseBody = await response.Content.ReadAsStringAsync();
            JsonDocument json = JsonDocument.Parse(responseBody);
            var repos = json.RootElement.EnumerateArray();

            if (!repos.Any())
            {
                Console.WriteLine("✅ Всі репозиторії оброблено.");
                break;
            }

            foreach (JsonElement repo in repos)
            {
                string repoName = repo.GetProperty("name").GetString();
                bool isPrivate = repo.GetProperty("private").GetBoolean();

                Console.WriteLine($"🔄 Перенос репозиторію {repoName}...");

                bool created = await CreateRepository(targetUsername, targetToken, repoName, isPrivate);
                if (created)
                {
                    await TransferRepositoryContent(sourceUsername, sourceToken, targetUsername, targetToken, repoName);
                    await CreateIssue(targetUsername, targetToken, repoName);
                }
            }

            page++;
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

        HttpContent content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        HttpResponseMessage response = await client.PostAsync(url, content);
        if (response.IsSuccessStatusCode)
        {
            Console.WriteLine($"✅ Репозиторій {repoName} створений.");
        }
        else
        {
            Console.WriteLine($"❌ Помилка створення {repoName}: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
        }

        return response.IsSuccessStatusCode;
    }

    static async Task TransferRepositoryContent(string sourceUsername, string sourceToken, string targetUsername, string targetToken, string repoName)
    {
        string tempDir = $"temp_repo_{repoName}";
        string sourceRepoUrl = $"https://{sourceUsername}:{sourceToken}@github.com/{sourceUsername}/{repoName}.git";
        string targetRepoUrl = $"https://{targetUsername}:{targetToken}@github.com/{targetUsername}/{repoName}.git";

        Console.WriteLine($"🔁 Клонування {repoName}...");
        await RunGitCommand($"clone --bare {sourceRepoUrl} {tempDir}");

        Console.WriteLine($"🚀 Відправка {repoName} у {targetUsername}...");
        await RunGitCommand($"push --mirror {targetRepoUrl}", tempDir);

        Console.WriteLine($"🗑️ Видалення тимчасового репозиторію...");
        await SafeDeleteDirectory(tempDir);
    }

    static async Task CreateIssue(string username, string token, string repoName)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        string url = $"https://api.github.com/repos/{username}/{repoName}/issues";
        string jsonBody = JsonSerializer.Serialize(new
        {
            title = "Автоматичний Issue",
            body = "Це створено скриптом для тестування."
        });

        HttpContent content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        HttpResponseMessage response = await client.PostAsync(url, content);
        if (response.IsSuccessStatusCode)
        {
            Console.WriteLine($"✅ Issue створено в {repoName}");
        }
        else
        {
            Console.WriteLine($"❌ Помилка створення Issue: {response.StatusCode}");
        }
    }

    static async Task RunGitCommand(string command, string workingDirectory = "")
    {
        using (var process = new Process())
        {
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = string.IsNullOrEmpty(workingDirectory) ? Environment.CurrentDirectory : workingDirectory
            };

            process.Start();
            await process.WaitForExitAsync();
        }
    }

    static async Task SafeDeleteDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath)) return;

        for (int i = 0; i < 5; i++)
        {
            try
            {
                Directory.Delete(directoryPath, true);
                Console.WriteLine($"✅ Каталог {directoryPath} успішно видалено.");
                return;
            }
            catch (IOException)
            {
                Console.WriteLine($"⚠️ Каталог {directoryPath} зайнятий. Повтор спроби...");
                await Task.Delay(1000);
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine($"⛔ Недостатньо прав для видалення {directoryPath}. Повтор через 1 сек...");
                await Task.Delay(1000);
            }
        }

        Console.WriteLine($"❌ Не вдалося видалити каталог {directoryPath}.");
    }
}
