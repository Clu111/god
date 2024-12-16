using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

class Program // Объявление основного класса программы
{
    // Объявление переменных
    private static ITelegramBotClient client;
    private static ReceiverOptions receiverOptions;
    private static string token = "8137272571:AAF7a4muYUOrOXPW3UifwcKOIbMAkz9j0NI"; // Токен для аутентификации бота в Telegram
    private static string jsonFilePath = "raffles.json";
    private static string historyJsonFilePath = "raffleHistory.json";
    private static List<Raffle> raffles = new List<Raffle>();
    private static List<Raffle> raffleHistory = new List<Raffle>();
    private static Timer raffleTimer;
    private static TimeSpan checkInterval = TimeSpan.FromSeconds(10); // Уменьшаем интервал для тестирования
    private static Dictionary<long, string> userStates = new Dictionary<long, string>();
    private static CallbackQuery callbackQuery;

    public static void Main(string[] args)
    {
        InitializeBot();
        LoadRaffles();
        ScheduleRaffleCheck();

        using var cts = new CancellationTokenSource();
        client.StartReceiving(UpdateHandler, ErrorHandler, receiverOptions, cts.Token);

        Console.WriteLine("Бот работает в автономном режиме!");
        Console.ReadLine();
        Console.WriteLine("Бот остановлен полностью");
    }

    private static void InitializeBot()
    {
        client = new TelegramBotClient(token);
        receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery }
        };
    }

    private static void ScheduleRaffleCheck()
    {
        raffleTimer = new Timer(CheckRaffles, null, TimeSpan.Zero, checkInterval);
    }

    private static void CheckRaffles(object state)
    {
        var rafflesToDraw = raffles.Where(r => r.ScheduledTime <= DateTime.Now.TimeOfDay).ToList();
        foreach (var raffle in rafflesToDraw)
        {
            _ = SelectWinner(client, raffle);
            raffles.Remove(raffle);
            raffleHistory.Add(raffle);
        }
        SaveRaffles();
        SaveRaffleHistory();
    }

    private static async Task UpdateHandler(ITelegramBotClient client, Update update, CancellationToken token)
    {
        if (update.Type == UpdateType.Message && update.Message?.Text != null)
        {
            await HandleMessageUpdate(client, update.Message);
        }
        else if (update.Type == UpdateType.CallbackQuery)
        {
            callbackQuery = update.CallbackQuery;
            await HandleCallbackQuery(client, update.CallbackQuery);
        }
    }
    private static bool IsAdmin(long chatId)
    {
        List<long> adminIds = new List<long> { 1910620149 }; // Замените на ваши ID администраторов
        return adminIds.Contains(chatId);
    }

    private static async Task StartCommand(ITelegramBotClient client, Message message)
    {
        await client.SendTextMessageAsync(message.Chat.Id, $"Привет {message.From?.Username}!");
        var startKeyboard = new InlineKeyboardMarkup(new[]
        {
            InlineKeyboardButton.WithCallbackData("История", "show_history")
        });
        await client.SendTextMessageAsync(message.Chat.Id, "История розыгрышей", replyMarkup: startKeyboard);
        await ShowRaffles(client, message.Chat.Id);
    }

    private static async Task HandleAdminCommands(ITelegramBotClient client, Message message)
    {
        string command = message.Text.Split(' ').First();
        switch (command)
        {
            case "/admin":
                await ShowAdminPanel(client, message);
                break;
            case "/create":
                await CreateRaffle(client, message);
                break;
            case "/history":
                await ShowRaffleHistory(client, message.Chat.Id);
                break;
            case "/delete":
                await DeleteRaffle(client, message);
                break;
            case "/setimage":
                await SetRaffleImage(client, message);
                break;
            case "/edit":
                await EditRaffleName(client, message);
                break;
            case "/settime":
                await SetRaffleTime(client, message);
                break;
            case "/start":
                await StartRaffle(client, message);
                break;
        }
    }

    private static async Task ShowAdminPanel(ITelegramBotClient client, Message message)
    {
        var chatId = message.Chat.Id;
        await client.SendTextMessageAsync(chatId, "Вы в панели администратора.", replyMarkup: AdminPanel());
    }

    private static InlineKeyboardMarkup AdminPanel()
    {
        return new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("Запустить розыгрыш", "start") },
            new[] { InlineKeyboardButton.WithCallbackData("Создать розыгрыш", "create") },
            new[] { InlineKeyboardButton.WithCallbackData("История розыгрышей", "history") },
            new[] { InlineKeyboardButton.WithCallbackData("Удалить розыгрыш", "delete") },
            new[] { InlineKeyboardButton.WithCallbackData("Задать картинку розыгрышу", "setimage") },
            new[] { InlineKeyboardButton.WithCallbackData("Показать розыгрыши", "show_raffles") }
        });
    }

    private static async Task CreateRaffle(ITelegramBotClient client, Message message)
    {
        string giveawayName = message.Text.Trim();
        var raffle = new Raffle(giveawayName);
        raffles.Add(raffle);
        SaveRaffles();
        Console.WriteLine($"Список розыгрышей после создания: {JsonSerializer.Serialize(raffles)}"); // Добавлено
        await client.SendTextMessageAsync(message.Chat.Id, $"Розыгрыш '{giveawayName}' создан!");
    }

    private static async Task DeleteRaffle(ITelegramBotClient client, Message message)
    {
        var parts = message.Text.Trim().Split(' ');
        if (parts.Length < 2)
        {
            await client.SendTextMessageAsync(message.Chat.Id, "Укажите название розыгрыша после команды /delete");
            return;
        }

        string giveawayName = string.Join(" ", parts.Skip(1));
        var raffle = raffles.FirstOrDefault(r => r.Name.Equals(giveawayName, StringComparison.OrdinalIgnoreCase));

        if (raffle != null)
        {
            raffles.Remove(raffle);
            SaveRaffles();
            await client.SendTextMessageAsync(message.Chat.Id, $"Розыгрыш '{giveawayName}' удалён.");
        }
        else
        {
            await client.SendTextMessageAsync(message.Chat.Id, $"Розыгрыш '{giveawayName}' не найден.");
        }
    }

    private static async Task SetRaffleImage(ITelegramBotClient client, Message message)
    {
        var parts = message.Text.Trim().Split(' ');
        if (parts.Length < 3)
        {
            await client.SendTextMessageAsync(message.Chat.Id, "Использование: /setimage имя URL");
            return;
        }

        string raffleName = parts[1];
        string imageUrl = parts[2];
        await UpdateRaffleProperty(client, message, raffleName, r => r.ImageURL = imageUrl,
            $"Картинка для розыгрыша \"{raffleName}\" установлена.");
    }

    private static async Task EditRaffleName(ITelegramBotClient client, Message message)
    {
        var parts = message.Text.Trim().Split(' ');
        if (parts.Length < 3)
        {
            await client.SendTextMessageAsync(message.Chat.Id, "Использование: /edit OLDname NEWname");
            return;
        }

        string oldName = parts[1];
        string newName = string.Join(" ", parts.Skip(2));
        await UpdateRaffleProperty(client, message, oldName, r => r.Name = newName,
            $"Название розыгрыша изменено на '{newName}'.");
    }

    private static async Task SetRaffleTime(ITelegramBotClient client, Message message)
    {
        var parts = message.Text.Trim().Split(' ');
        if (parts.Length < 3)
        {
            await client.SendTextMessageAsync(message.Chat.Id, "Использование: /settime имя HH:mm");
            return;
        }

        string raffleName = parts[1];
        string timeStr = parts[2];
        if (!TimeSpan.TryParse(timeStr, out var scheduledTime))
        {
            await client.SendTextMessageAsync(message.Chat.Id, "Укажите время в формате HH:mm");
            return;
        }

        await UpdateRaffleProperty(client, message, raffleName, r => r.ScheduledTime = scheduledTime,
            $"Время розыгрыша \"{raffleName}\" установлено на {timeStr}");
    }

    private static async Task StartRaffle(ITelegramBotClient client, Message message)
    {
        var parts = message.Text.Trim().Split(' ');
        if (parts.Length < 2)
        {
            await client.SendTextMessageAsync(message.Chat.Id, "Укажите название розыгрыша после команды /start");
            return;
        }

        string raffleName = string.Join(" ", parts.Skip(1));
        var raffle = raffles.FirstOrDefault(r => r.Name.Equals(raffleName, StringComparison.OrdinalIgnoreCase));

        if (raffle != null)
        {
            if (raffle.ScheduledTime <= DateTime.Now.TimeOfDay)
            {
                await SelectWinner(client, raffle);
                raffles.Remove(raffle);
                raffleHistory.Add(raffle);
                SaveRaffles();
                SaveRaffleHistory();
            }
            else
            {
                await client.SendTextMessageAsync(message.Chat.Id, $"Розыгрыш \"{raffle.Name}\" не может быть запущен до {raffle.ScheduledTime}");
            }
        }
        else
        {
            await client.SendTextMessageAsync(message.Chat.Id, $"Розыгрыш '{raffleName}' не найден.");
        }
    }

    private static async Task UpdateRaffleProperty(ITelegramBotClient client, Message message, string raffleName, Action<Raffle> updateAction, string successMessage)
    {
        var raffle = raffles.FirstOrDefault(r => r.Name.Equals(raffleName, StringComparison.OrdinalIgnoreCase));
        if (raffle != null)
        {
            updateAction(raffle);
            SaveRaffles();
            await client.SendTextMessageAsync(message.Chat.Id, successMessage);
        }
        else
        {
            await client.SendTextMessageAsync(message.Chat.Id, $"Розыгрыш '{raffleName}' не найден.");
        }
    }

    private static async Task HandleCallbackQuery(ITelegramBotClient client, CallbackQuery callbackQuery)
    {
        if (callbackQuery?.Data == null) return;

        await client.AnswerCallbackQueryAsync(callbackQuery.Id, "Запрос обрабатывается...");

        long chatId = callbackQuery.Message.Chat.Id;
        string data = callbackQuery.Data;

        if (data.StartsWith("show_raffle_details_"))
        {
            string raffleName = data.Substring("show_raffle_details_".Length);
            var raffle = FindRaffle(raffleName);
            if (raffle != null)
            {
                await ShowRaffleDetails(client, chatId, raffle);
            }
            else
            {
                await client.AnswerCallbackQueryAsync(callbackQuery.Id, "Розыгрыш не найден.");
            }
        }
        else if (data.StartsWith("participate_"))
        {
            string raffleName = data.Substring("participate_".Length);
            await HandleParticipantActionAsync(client, chatId, callbackQuery.Message.MessageId, "participate", raffleName, callbackQuery.From.Id, callbackQuery.From.Username);
        }
        else if (data.StartsWith("withdraw_"))
        {
            string raffleName = data.Substring("withdraw_".Length);
            await HandleParticipantActionAsync(client, chatId, callbackQuery.Message.MessageId, "withdraw", raffleName, callbackQuery.From.Id, callbackQuery.From.Username);
        }
        else
        {

            Console.WriteLine($"Обрабатываемый CallbackQuery: {callbackQuery.Data}"); // Добавлено для отладки

            switch (callbackQuery.Data)
            {
                case "create":
                    await client.SendTextMessageAsync(chatId, "Введите название нового розыгрыша:");
                    userStates[chatId] = "create";
                    break;
                case "start":
                    await client.SendTextMessageAsync(chatId, "Введите название розыгрыша для запуска:");
                    userStates[chatId] = "start";
                    break;
                case "delete":
                    await client.SendTextMessageAsync(chatId, "Введите название розыгрыша для удаления:");
                    userStates[chatId] = "delete";
                    break;
                case "setimage":
                    await client.SendTextMessageAsync(chatId, "Введите название розыгрыша и URL картинки через пробел:");
                    userStates[chatId] = "setimage";
                    break;
                case "show_history":
                    await ShowRaffleHistory(client, chatId);
                    break;
                case "history":
                    await ShowRaffleHistory(client, chatId);
                    break;
                case "close":
                    // Добавлена обработка кнопки "Назад"
                    await client.AnswerCallbackQueryAsync(callbackQuery.Id, "Возврат в главное меню");
                    await client.DeleteMessageAsync(chatId, callbackQuery.Message.MessageId);
                    await ShowRaffles(client, chatId);
                    break;
                case "show_raffles":
                    await ShowRaffles(client, chatId);
                    break;
                default:
                    break;
            }
        }
    }

    private static Raffle FindRaffle(string raffleName)
    {
        Console.WriteLine($"FindRaffle получил: {raffleName}"); // Отладка
        raffleName = raffleName.ToLowerInvariant();
        var raffle = raffles.FirstOrDefault(r => r.Name.ToLowerInvariant().Contains(raffleName)) ??
               raffleHistory.FirstOrDefault(r => r.Name.ToLowerInvariant().Contains(raffleName));
        Console.WriteLine($"FindRaffle вернул: {raffle?.Name}"); // Отладка
        return raffle;
    }

    private static async Task ShowRaffleHistory(ITelegramBotClient client, long chatId)
    {
        if (!raffleHistory.Any())
        {
            await client.SendTextMessageAsync(chatId, "История розыгрышей пуста.");
            return;
        }

        var buttons = raffleHistory.Select(r => InlineKeyboardButton.WithCallbackData(r.Name, $"show_raffle_details_{r.Name}")).ToList();
        var keyboard = new InlineKeyboardMarkup(buttons.Concat(new[] { InlineKeyboardButton.WithCallbackData("Закрыть", "close") }));
        await client.SendTextMessageAsync(chatId, "Выберите розыгрыш из истории:", replyMarkup: keyboard);
    }

    private static async Task ShowRaffles(ITelegramBotClient client, long chatId)
    {
        if (!raffles.Any())
        {
            await client.SendTextMessageAsync(chatId, "На данный момент розыгрыши недоступны.");
            return;
        }

        var buttons = raffles.Select(r => {
            string callbackData = $"show_raffle_details_{r.Name}";
            Console.WriteLine($"Создаем кнопку для {r.Name}, callback data: {callbackData}"); // Добавлено для отладки
            return InlineKeyboardButton.WithCallbackData(r.Name, callbackData);
        }).ToList();
        var keyboard = new InlineKeyboardMarkup(buttons.Concat(new[] { InlineKeyboardButton.WithCallbackData("Назад", "close") }));
        await client.SendTextMessageAsync(chatId, "Доступные розыгрыши:", replyMarkup: keyboard);
    }

    private static async Task HandleParticipantActionAsync(ITelegramBotClient client, long chatId, long messageId, string action, string raffleName, long participantId, string username)
    {
        var raffle = raffles.FirstOrDefault(r => r.Name.Equals(raffleName, StringComparison.OrdinalIgnoreCase));
        if (raffle == null)
        {
            await client.AnswerCallbackQueryAsync(callbackQuery.Id, "Розыгрыш не найден.");
            return;
        }

        if (action == "participate")
        {
            await ParticipateInRaffle(raffle, participantId, username);
        }
        else if (action == "withdraw")
        {
            await WithdrawFromRaffle(raffle, participantId, username);
        }

        await UpdateRaffleMessage(client, chatId, messageId, raffle);
        _ = Task.Run(() => SaveRafflesAsync());
    }

    private static async Task ParticipateInRaffle(Raffle raffle, long participantId, string username)
    {
        if (!raffle.ParticipantIds.Contains(participantId))
        {
            raffle.ParticipantIds.Add(participantId);
            raffle.Participants.Add(username ?? "Аноним");
            SaveRaffles();
        }
    }

    private static async Task WithdrawFromRaffle(Raffle raffle, long participantId, string username)
    {
        if (raffle.ParticipantIds.Contains(participantId))
        {
            raffle.ParticipantIds.Remove(participantId);
            raffle.Participants.RemoveAll(p => p == username);
            SaveRaffles();
        }
    }


    private static async Task UpdateRaffleMessage(ITelegramBotClient client, long chatId, long messageId, Raffle raffle)
    {
        var keyboard = RaffleActionButtons(raffle.Name);
        string messageText = $"Розыгрыш: {raffle.Name}\nКоличество участников: {raffle.Participants.Count}\nЗапланированное время: {raffle.ScheduledTime}";
        await client.EditMessageTextAsync(chatId, (int)messageId, messageText, replyMarkup: keyboard);
    }

    private static async Task SaveRafflesAsync()
    {
        var json = JsonSerializer.Serialize(raffles);
        await System.IO.File.WriteAllTextAsync(jsonFilePath, json); // System.IO.File указан явно
    }

    private static async Task ShowRaffleDetails(ITelegramBotClient client, long chatId, Raffle raffle)
    {
        var keyboard = RaffleActionButtons(raffle.Name); // Создаем клавиатуру с кнопками
        string messageText = $"Розыгрыш: {raffle.Name}\nКоличество участников: {raffle.Participants.Count}\nЗапланированное время: {raffle.ScheduledTime}";
        Console.WriteLine($"MessageId set to: {raffle.MessageId}"); // Проверка MessageId
        if (!string.IsNullOrEmpty(raffle.ImageURL))
        {
            var message = await SendRafflePhoto(client, chatId, raffle, messageText, keyboard);
            raffle.MessageId = message.MessageId;
        }
        else
        {
            var message = await client.SendTextMessageAsync(chatId, messageText, replyMarkup: keyboard); // Добавили replyMarkup
            raffle.MessageId = message.MessageId;
        }
    }

    private static async Task<Message> SendRafflePhoto(ITelegramBotClient client, long chatId, Raffle raffle, string messageText, InlineKeyboardMarkup keyboard)
    {
        try
        {
            var photo = new InputFileUrl(raffle.ImageURL);
            return await client.SendPhotoAsync(chatId, photo, caption: messageText, replyMarkup: keyboard);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при отправке изображения: {ex.Message}");
            var message = await client.SendTextMessageAsync(chatId, messageText, replyMarkup: keyboard);
            return message;
        }
    }

    private static async Task HandleMessageUpdate(ITelegramBotClient client, Message message)
    {
        var chatId = message.Chat.Id;
        if (message.Text == "/start")
        {
            await StartCommand(client, message);
        }
        else if (IsAdmin(chatId))
        {
            if (userStates.ContainsKey(chatId))
            {
                await HandlePendingAdminAction(client, message);
            }
            else
            {
                await HandleAdminCommands(client, message);
            }
        }
        else
        {
            await client.SendTextMessageAsync(chatId, "** вы не администратор **");
        }
    }

    private static async Task HandlePendingAdminAction(ITelegramBotClient client, Message message)
    {
        string expectedAction = userStates[message.Chat.Id];
        switch (expectedAction)
        {
            case "create":
                await CreateRaffle(client, message);
                userStates.Remove(message.Chat.Id);
                break;
            case "start":
                await StartRaffle(client, message);
                userStates.Remove(message.Chat.Id);
                break;
            case "delete":
                await DeleteRaffle(client, message);
                userStates.Remove(message.Chat.Id);
                break;
            case "setimage":
                await SetRaffleImage(client, message);
                userStates.Remove(message.Chat.Id);
                break;
        }
    }

    private static InlineKeyboardMarkup RaffleActionButtons(string raffleName)
    {
        return new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("Участвовать", $"participate_{raffleName}"),
                InlineKeyboardButton.WithCallbackData("Отписаться", $"withdraw_{raffleName}")
            }
        });
    }

    private static void LoadRaffles()
    {
        if (System.IO.File.Exists(jsonFilePath)) // System.IO.File указан явно
        {
            var json = System.IO.File.ReadAllText(jsonFilePath); // System.IO.File указан явно
            raffles = JsonSerializer.Deserialize<List<Raffle>>(json) ?? new List<Raffle>();
        }

        if (System.IO.File.Exists(historyJsonFilePath)) // System.IO.File указан явно
        {
            var json = System.IO.File.ReadAllText(historyJsonFilePath); // System.IO.File указан явно
            raffleHistory = JsonSerializer.Deserialize<List<Raffle>>(json) ?? new List<Raffle>();
        }
    }

    private static void SaveRaffles()
    {
        var json = JsonSerializer.Serialize(raffles);
        System.IO.File.WriteAllText(jsonFilePath, json); // System.IO.File указан явно
    }

    private static void SaveRaffleHistory()
    {
        var json = JsonSerializer.Serialize(raffleHistory);
        System.IO.File.WriteAllText(historyJsonFilePath, json); // System.IO.File указан явно
    }

    private static Task ErrorHandler(ITelegramBotClient client, Exception exception, CancellationToken token)
    {
        Console.WriteLine($"Ошибка: {exception.Message}");
        return Task.CompletedTask;
    }

    private static async Task SelectWinner(ITelegramBotClient client, Raffle raffle)
    {
        if (!raffle.Participants.Any())
        {
            await NotifyNoParticipants(client, raffle);
            return;
        }

        raffle.RaffleTime = DateTime.Now;
        int winnerIndex = new Random().Next(raffle.Participants.Count);
        string winnerName = raffle.Participants[winnerIndex];
        long winnerId = raffle.ParticipantIds[winnerIndex];

        string messageText = $"Поздравляем! Вы победитель розыгрыша '{raffle.Name}'!";
        await client.SendTextMessageAsync((int)winnerId, messageText);
        await NotifyAllParticipants(client, raffle, winnerId, winnerName);
    }

    private static async Task NotifyNoParticipants(ITelegramBotClient client, Raffle raffle)
    {
        string noParticipantsMessage = $"В розыгрыше '{raffle.Name}' нет участников.";
        foreach (var participantId in raffle.ParticipantIds)
        {
            await client.SendTextMessageAsync((int)participantId, noParticipantsMessage);
        }
    }

    private static async Task NotifyAllParticipants(ITelegramBotClient client, Raffle raffle, long winnerId, string winnerName)
    {
        string participantList = string.Join(", ", raffle.Participants);
        foreach (var participantId in raffle.ParticipantIds)
        {
            string resultMessage = participantId == winnerId
                ? $"Поздравляем, вы победили в розыгрыше '{raffle.Name}'."
                : $"Вы не выиграли в розыгрыше '{raffle.Name}'. Спасибо за участие!";
            await client.SendTextMessageAsync((int)participantId, resultMessage);
        }

        foreach (var participantId in raffle.ParticipantIds)
        {
            await client.SendTextMessageAsync((int)participantId, $"Результаты розыгрыша '{raffle.Name}'\nПобедитель - {winnerName}.\n\nПолный список участников: {participantList}");
        }
    }

    public class Raffle
    {
        public string Name { get; set; }
        public List<string> Participants { get; set; }
        public List<long> ParticipantIds { get; set; }
        public TimeSpan? ScheduledTime { get; set; }
        public DateTime? RaffleTime { get; set; }
        public string ImageURL { get; set; }
        public long MessageId { get; set; }

        public Raffle(string name)
        {
            Name = name;
            Participants = new List<string>();
            ParticipantIds = new List<long>();
        }
    }
}