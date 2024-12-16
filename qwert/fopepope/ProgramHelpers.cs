using System.Text.Json;

internal static class ProgramHelpers
{
    public static async Task SaveRaffleHistoryAsync(List<Program.Raffle> raffleHistory, string historyJsonFilePath)
    {
        var json = JsonSerializer.Serialize(raffleHistory);
        await System.IO.File.WriteAllTextAsync(historyJsonFilePath, json);
    }
}