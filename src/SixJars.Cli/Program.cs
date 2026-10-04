using System.Collections;
using SixJars.Cli;

// 只負責把 process 的環境變數交給 CliApp；其餘邏輯都在 CliApp，測試直接在 process 內呼叫。
var environment = Environment.GetEnvironmentVariables()
    .Cast<DictionaryEntry>()
    .ToDictionary(e => (string)e.Key, e => (string?)e.Value);
return await CliApp.RunAsync(args, environment, Console.Out, CancellationToken.None);
