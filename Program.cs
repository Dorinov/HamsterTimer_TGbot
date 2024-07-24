using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using static System.Net.Mime.MediaTypeNames;

class Program
{
    private static TelegramBotClient botClient = new TelegramBotClient("7467683601:AAFaLXuw1u0yCEAvzX3vAZcAPYfL-wOXd-s");
    private static Dictionary<long, UserData> userData = new Dictionary<long, UserData>();

    private static string ud_file = Environment.CurrentDirectory + "/userData.json";
    private static string log_file = Environment.CurrentDirectory + "/log.txt";

    static async Task Main(string[] args)
    {
        LoadUserData();
        botClient.StartReceiving(UpdateHandler, ErrorHandler);
        SaveUserData();

        msg("Bot started");

        while (true)
        {
            var str = Console.ReadLine();
            await HandleConsoleCommand(str);
        }
    }

    private static async Task HandleConsoleCommand(string str)
    {
        var cmd = str.Split(' ');

        if (cmd[0] == "exit")
            Environment.Exit(0);

        if (cmd.Length >= 3 && cmd[0] == "msg")
        {
            var text = string.Join(" ", cmd, 2, cmd.Length - 2);
            await SendMessage(cmd[1], text);
            return;
        }

        if (cmd.Length >= 2 && cmd[0] == "admin")
        {
            AdminChange(cmd[1]);
            return;
        }

        if (cmd.Length >= 2 && cmd[0] == "ban")
        {
            BanUnban(cmd[1], true);
            return;
        }
        if (cmd.Length >= 2 && cmd[0] == "unban")
        {
            BanUnban(cmd[1], false);
            return;
        }

        msg("Unknown command, bitch");
    }

    private static void AdminChange(string id)
    {
        foreach (var user in userData.Values)
            if (user.UserName == id)
            {
                user.IsAdmin = !user.IsAdmin;
                msg($"Пользователь @{user.UserName} теперь " + (user.IsAdmin ? "админ" : "не админ"));
                break;
            }
    }

    private static void BanUnban(string name, bool value)
    {
        foreach (var user in userData.Values)
            if (user.UserName == name)
            {
                user.IsBanned = value;
                msg($"Пользователь @{user.UserName} {(value ? "за" : "раз")}банен");
                break;
            }
    }

    private static async Task SendMessage(string id, string message)
    {
        if (id != "all")
        {
            foreach (var user in userData.Values)
                if (user.UserName == id)
                {
                    await botClient.SendTextMessageAsync(user.UserId, message);
                    break;
                }
        }
        else
            foreach (var user in userData.Values)
                await botClient.SendTextMessageAsync(user.UserId, message);
        msg("Сообщение доставлено");
    }

    private static async Task UpdateHandler(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (update.Message == null || update.Message.Type != MessageType.Text)
            return;

        msg($"{update.Message.Chat.Username} (#{update.Message.Chat.Id}): {update.Message.Text}");

        var userId = update.Message.Chat.Id;
        var text = update.Message.Text;

        if (!userData.ContainsKey(userId))
        {
            var newUser = new UserData();
            newUser.UserId = userId;
            newUser.UserName = update.Message.Chat.Username;
            if (newUser.UserName == "dorinov")
                newUser.IsAdmin = true;
            userData.Add(userId, newUser);
            await botClient.SendTextMessageAsync(userId, "Привет 🙋\r\nЯ бот, который будет отсчитывать для тебя время ⏰\r\n \r\nНапиши мне последний временной поинт в формате '13:46' 👇");
        }

        if (text == "/start")
        {
            SaveUserData();
            return;
        }

        var user = userData[userId];

        if (user.IsBanned)
            return;

        if (user.IsAdmin)
            if (await AdminCommandHandler(userId, text.Replace("@", "")))
                return;

        if (text == "/delete")
        {
            user.StartTime = default;
            user.Interval = default;
            user.State = UserState.WaitingForTime;
            await botClient.SendTextMessageAsync(userId, "Таймер удален. Если хочешь добавить новый, напиши последний временной поинт 🙃");

            SaveUserData();
            return;
        }

        if (user.State == UserState.WaitingForTime)
        {
            if (DateTime.TryParse(text, out DateTime time))
            {
                user.StartTime = time;
                user.State = UserState.WaitingForInterval;
                await botClient.SendTextMessageAsync(userId, "Отлично! Теперь напиши мне интервал в минутах 👇");
            }
            else
            {
                await botClient.SendTextMessageAsync(userId, "Бля, чет я не понял, че ты высрал. Давай еще раз, ток нормально..");
            }

            SaveUserData();
            return;
        }

        if (user.State == UserState.WaitingForInterval)
        {
            if (int.TryParse(text, out int interval))
            {
                user.Interval = interval;
                user.State = UserState.Idle;

                var delay = user.StartTime.AddMinutes(interval) - DateTime.Now;
                var time = delay.Hours + " ч. " + delay.Minutes + " мин.";
                await botClient.SendTextMessageAsync(userId, $"Время и интервал сохранены. Пришлю уведомление через {time} 😋");

                StartNotificationTimer(userId, user);
            }
            else
            {
                await botClient.SendTextMessageAsync(userId, "Ниче не понял. Напиши интервал в минутах, сучка 😡");
            }

            SaveUserData();
            return;
        }

        if (user.State == UserState.WaitingForResponse)
        {
            user.State = UserState.Idle;

            var adtime = DateTime.Now.AddMinutes(user.Interval).ToString("HH:mm");
            await botClient.SendTextMessageAsync(userId, $"Отлично, ожидай следующих уведомлений в {adtime} 😉", replyMarkup: new ReplyKeyboardRemove());

            user.StartTime = DateTime.Now;
            StartNotificationTimer(userId, user);

            SaveUserData();
            return;
        }
    }

    private static async Task<bool> AdminCommandHandler(long userId, string text)
    {
        if (text.ToLower() == "get users")
        {
            var users = "Вот список всех пользователей:\r\n";
            foreach (var u in userData.Values)
                users += $"@{u.UserName}" + (u.IsAdmin ? " (adm)" : "") + (u.IsBanned ? " (ban)\r\n" : "\r\n");
            await botClient.SendTextMessageAsync(userId, users);
            return true;
        }
        if (text.ToLower() == "get log")
        {
            if (System.IO.File.Exists(log_file))
            {
                using (var fileStream = new FileStream(log_file, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    InputFileStream inputFileStream = new InputFileStream(fileStream, Path.GetFileName(log_file));
                    await botClient.SendDocumentAsync(userId, inputFileStream);
                }
                System.IO.File.Delete(log_file);
            }
            else
                await botClient.SendTextMessageAsync(userId, "Файл лога пуст.");
            return true;
        }
        if (text.ToLower() == "get timers")
        {
            var txt = "Вот список всех таймеров пользователей:\r\n";
            foreach (var user in userData.Values)
            {
                var a = user.StartTime.ToString("HH:mm");
                txt += $"@{user.UserName}\r\n    последний поинт: {a}\r\n    интервал: {user.Interval}\r\n";
            }
            await botClient.SendTextMessageAsync(userId, txt);
            return true;
        }

        var cmd = text.Split(' ');
        if (cmd[0].ToLower() == "msg")
        {
            var mes = string.Join(" ", cmd, 2, cmd.Length - 2);
            await SendMessage(cmd[1], mes);
            return true;
        }
        if (cmd[0].ToLower() == "admin")
        {
            if (cmd[1].ToLower() != "dorinov")
                AdminChange(cmd[1]);
            else
                await botClient.SendTextMessageAsync(userId, "Пасаси");
            return true;
        }
        if (cmd[0].ToLower() == "ban")
        {
            if (cmd[1].ToLower() != "dorinov")
                BanUnban(cmd[1], true);
            else
                await botClient.SendTextMessageAsync(userId, "Пасаси");
            return true;
        }
        if (cmd[0].ToLower() == "unban")
        {
            BanUnban(cmd[1], false);
            return true;
        }
        return false;
    }

    private static Task ErrorHandler(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        msg("Error: " + exception.Message);
        return Task.CompletedTask;
    }

    private static async void StartNotificationTimer(long userId, UserData user)
    {
        var kb = new ReplyKeyboardMarkup(new[] { new KeyboardButton[] { "Зашел 💸" } });
        kb.ResizeKeyboard = true;

        var a = user.StartTime;
        var b = user.Interval;

        var delay = user.StartTime.AddMinutes(user.Interval) - DateTime.Now;
        if (delay > TimeSpan.Zero)
        {
            var dividedDelay = new TimeSpan(delay.Ticks / 4);
            for (var i = 0; i < 4; i++)
            {
                if (a != user.StartTime || b != user.Interval)
                    break;
                await Task.Delay(dividedDelay);
            }
        }

        if (a == user.StartTime && b == user.Interval)
        {
            user.State = UserState.WaitingForResponse;

            SaveUserData();

            msg("Увед у #" + userId);
            await botClient.SendTextMessageAsync(userId, "Бро, зайди в <a href=\"https://t.me/hamster_koMbat_bot/start\">Hamster Kombat</a> ❗️",
                replyMarkup: kb, parseMode: ParseMode.Html, disableWebPagePreview: true);
        }
    }

    private static void LoadUserData()
    {
        if (System.IO.File.Exists(ud_file))
        {
            string json = System.IO.File.ReadAllText(ud_file);
            userData = JsonSerializer.Deserialize<Dictionary<long, UserData>>(json);

            foreach (var user in userData.Values)
                if (user.State == UserState.Idle)
                    StartNotificationTimer(user.UserId, user);
        }
    }

    private static void SaveUserData()
    {
        string json = JsonSerializer.Serialize(userData);
        System.IO.File.WriteAllText(ud_file, json);
    }

    static void msg(string t)
    {
        var str = DateTime.Now + " | " + t;
        Console.WriteLine(str);
        System.IO.File.AppendAllText(log_file, str + Environment.NewLine);
    }
}

enum UserState
{
    WaitingForTime,
    WaitingForInterval,
    Idle,
    WaitingForResponse
}

class UserData
{
    public long UserId { get; set; }
    public string UserName { get; set; }
    public DateTime StartTime { get; set; }
    public int Interval { get; set; }
    public UserState State { get; set; } = UserState.WaitingForTime;
    public bool IsAdmin { get; set; } = false;
    public bool IsBanned { get; set; } = false;
}