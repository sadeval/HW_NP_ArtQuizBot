using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace ArtQuizBot
{
    class Program
    {
        private static readonly string BotToken = "Your_Token";

        private static readonly TelegramBotClient botClient = new TelegramBotClient(BotToken);

        private static ConcurrentDictionary<long, UserSession> userSessions = new ConcurrentDictionary<long, UserSession>();

        private static ConcurrentDictionary<long, int> userScores = new ConcurrentDictionary<long, int>();

        private static ConcurrentDictionary<long, string> userNames = new ConcurrentDictionary<long, string>();

        static async Task Main()
        {
            LoadScores();
            LoadUserNames();

            Console.WriteLine("Запуск бота...");

            using var cts = new CancellationTokenSource();

            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = Array.Empty<UpdateType>() 
            };

            botClient.StartReceiving(
                updateHandler: HandleUpdateAsync,
                pollingErrorHandler: HandleErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: cts.Token
            );

            var me = await botClient.GetMeAsync();
            Console.WriteLine($"Бот {me.Username} запущен.");

            Console.WriteLine("Нажмите Enter для остановки бота.");
            Console.ReadLine();

            SaveScores();
            SaveUserNames();
            cts.Cancel();
        }

        public static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            if (update.Type != UpdateType.Message)
                return;

            var message = update.Message;
            if (message == null || message.Type != MessageType.Text)
                return;

            long chatId = message.Chat.Id;
            string? userMessage = message.Text;
            string userName = message.Chat.FirstName ?? "Пользователь"; 

            if (userMessage == "/start")
            {
                await StartQuiz(chatId, userName);
            }
            else if (userMessage == "/score")
            {
                await ShowScores(chatId);
            }
            else
            {
                await ProcessAnswer(chatId, userMessage);
            }
        }

        public static Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            Console.WriteLine($"Ошибка: {exception.Message}");
            return Task.CompletedTask;
        }

        private static async Task StartQuiz(long chatId, string userName)
        {
            userNames.TryAdd(chatId, userName);

            var questions = QuizLoader.GetQuestions();

            var session = new UserSession
            {
                CurrentQuestionIndex = 0,
                Score = 0,
                Questions = questions
            };

            userSessions[chatId] = session;

            await botClient.SendTextMessageAsync(chatId, "🎉 Добро пожаловать в викторину по искусству!");
            await SendQuestion(chatId, session);
        }

        private static async Task SendQuestion(long chatId, UserSession session)
        {
            if (session.CurrentQuestionIndex >= session.Questions.Count)
            {
                await botClient.SendTextMessageAsync(chatId, $"Викторина завершена! Ваш счет: {session.Score}");

                userScores.AddOrUpdate(chatId, session.Score, (key, oldValue) => oldValue + session.Score);

                await botClient.SendTextMessageAsync(chatId, "Чтобы начать заново, отправьте /start.\nЧтобы посмотреть свой рейтинг, отправьте /score.");

                userSessions.TryRemove(chatId, out _);
                return;
            }

            var question = session.Questions[session.CurrentQuestionIndex];

            var buttons = new List<KeyboardButton[]>();
            foreach (var option in question.Options)
            {
                buttons.Add(new[] { new KeyboardButton(option) });
            }

            var replyKeyboard = new ReplyKeyboardMarkup(buttons)
            {
                ResizeKeyboard = true,
                OneTimeKeyboard = true
            };

            await botClient.SendTextMessageAsync(chatId, question.Text, replyMarkup: replyKeyboard);

            if (!string.IsNullOrEmpty(question.ImageUrl))
            {
                var inputFile = new Telegram.Bot.Types.InputFiles.InputOnlineFile(new Uri(question.ImageUrl));
                await botClient.SendPhotoAsync(chatId, inputFile);
            }
        }

        private static async Task ProcessAnswer(long chatId, string userAnswer)
        {
            if (!userSessions.TryGetValue(chatId, out UserSession? session))
            {
                await botClient.SendTextMessageAsync(chatId, "Пожалуйста, начните викторину командой /start.");
                return;
            }

            var question = session.Questions[session.CurrentQuestionIndex];

            if (userAnswer == question.Options[question.CorrectOptionIndex])
            {
                session.Score++;
                await botClient.SendTextMessageAsync(chatId, "✅ Правильно!");
            }
            else
            {
                await botClient.SendTextMessageAsync(chatId, $"❌ Неправильно! Правильный ответ: {question.Options[question.CorrectOptionIndex]}");
            }

            if (!string.IsNullOrEmpty(question.Explanation))
            {
                await botClient.SendTextMessageAsync(chatId, question.Explanation);
            }

            session.CurrentQuestionIndex++;
            await SendQuestion(chatId, session);
        }

        private static async Task ShowScores(long chatId)
        {
            var topScores = userScores.OrderByDescending(u => u.Value).Take(10);
            string scoreMessage = "🏆 Топ участников:\n";

            int rank = 1;
            foreach (var (userId, score) in topScores)
            {
                string userName = userNames.TryGetValue(userId, out string? name) ? name : "Неизвестный";

                scoreMessage += $"{rank}. {userName}: {score} баллов\n";
                rank++;
            }

            await botClient.SendTextMessageAsync(chatId, scoreMessage);
        }
        
        public static void SaveScores()
        {
            string filePath = "scores.json";
            var jsonData = JsonConvert.SerializeObject(userScores, Formatting.Indented);
            System.IO.File.WriteAllText(filePath, jsonData);  
        }

        public static void LoadScores()
        {
            string filePath = "scores.json";
            if (System.IO.File.Exists(filePath)) 
            {
                string jsonData = System.IO.File.ReadAllText(filePath);  
                userScores = JsonConvert.DeserializeObject<ConcurrentDictionary<long, int>>(jsonData);
            }
        }

        public static void SaveUserNames()
        {
            string filePath = "usernames.json";
            var jsonData = JsonConvert.SerializeObject(userNames, Formatting.Indented);
            System.IO.File.WriteAllText(filePath, jsonData);  
        }

        public static void LoadUserNames()
        {
            string filePath = "usernames.json";
            if (System.IO.File.Exists(filePath)) 
            {
                string jsonData = System.IO.File.ReadAllText(filePath); 
                userNames = JsonConvert.DeserializeObject<ConcurrentDictionary<long, string>>(jsonData);
            }
        }
    }

    public static class QuizLoader
    {
        public static List<Question> GetQuestions()
        {
            string filePath = @"D:\SQL\HW_NP_ArtQuizBot\bin\Debug\net8.0\questions.json"; 

            if (!System.IO.File.Exists(filePath))
            {
                throw new FileNotFoundException("Файл с вопросами не найден.");
            }

            string jsonData = System.IO.File.ReadAllText(filePath);
            return JsonConvert.DeserializeObject<List<Question>>(jsonData);
        }
    }

    public class Question
    {
        public string? Text { get; set; }
        public List<string>? Options { get; set; }
        public int CorrectOptionIndex { get; set; }
        public string? Explanation { get; set; }
        public string? ImageUrl { get; set; }
    }

    public class UserSession
    {
        public int CurrentQuestionIndex { get; set; }
        public int Score { get; set; }
        public List<Question>? Questions { get; set; }
    }
}
