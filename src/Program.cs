using Discord;
using Discord.WebSocket;

class Program
{
    private static DiscordSocketClient _client = null!;
    private static byte[]? _logoBytes;

    // ── Hier alles anpassen ───────────────────────────────────────────────────
    const string SERVER_NAME      = "SERVER GEREINIGT"; // Servername nach dem Clean
    const string CHANNEL_NAME     = "information";       // Name der neuen Kanäle
    const int    CHANNEL_COUNT    = 3;                   // Anzahl der Kanäle
    const int    MESSAGE_COUNT    = 1;                   // Nachrichten pro Kanal
    const string MESSAGE_TEXT     =
        "✅ **Server erfolgreich Gereinigt!**\n" +
        "> Alle Kanäle und Rollen wurden entfernt.\n" +
        "> Powered by **Clean Your Server**";
    // ─────────────────────────────────────────────────────────────────────────

    static async Task Main()
    {
        var token = Environment.GetEnvironmentVariable("DISCORD_TOKEN");
        if (string.IsNullOrEmpty(token))
        {
            Console.Error.WriteLine("[FEHLER] DISCORD_TOKEN ist nicht gesetzt!");
            return;
        }

        if (File.Exists("/app/logo.jpg"))
        {
            _logoBytes = await File.ReadAllBytesAsync("/app/logo.jpg").ConfigureAwait(false);
            Console.WriteLine($"[OK] Logo vorgeladen ({_logoBytes.Length / 1024} KB).");
        }
        else
        {
            Console.WriteLine("[WARN] logo.jpg nicht gefunden – Logo-Änderung wird übersprungen.");
        }

        var config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds
                           | GatewayIntents.GuildMessages
                           | GatewayIntents.MessageContent
        };

        _client = new DiscordSocketClient(config);

        _client.Log             += LogAsync;
        _client.Ready           += ReadyAsync;
        _client.MessageReceived += MessageHandler;

        await _client.LoginAsync(TokenType.Bot, token).ConfigureAwait(false);
        await _client.StartAsync().ConfigureAwait(false);
        await Task.Delay(-1).ConfigureAwait(false);
    }

    private static Task LogAsync(LogMessage log)
    {
        Console.WriteLine(log.ToString());
        return Task.CompletedTask;
    }

    private static Task ReadyAsync()
    {
        Console.WriteLine("[OK] Bot ist online – !clean-myserver bereit.");
        return Task.CompletedTask;
    }

    private static async Task MessageHandler(SocketMessage message)
    {
        if (message is not SocketUserMessage userMsg) return;
        if (userMsg.Author.IsBot) return;
        if (userMsg.Channel is not SocketGuildChannel guildChannel) return;
        if (!userMsg.Content.Equals("!clean-myserver", StringComparison.OrdinalIgnoreCase)) return;

        var guildUser = guildChannel.Guild.GetUser(userMsg.Author.Id);
        if (!guildUser.GuildPermissions.Administrator)
        {
            await userMsg.Channel.SendMessageAsync(
                "❌ Du brauchst Administrator-Rechte für diesen Command."
            ).ConfigureAwait(false);
            return;
        }

        var guild = guildChannel.Guild;

        await userMsg.Channel.SendMessageAsync("🧹 Reinigung läuft...").ConfigureAwait(false);

        // ── ALLES GLEICHZEITIG ────────────────────────────────────────────────
        var editTask = guild.ModifyAsync(p =>
        {
            p.Name = SERVER_NAME;
            if (_logoBytes is not null)
                p.Icon = new Image(new MemoryStream(_logoBytes));
        });

        var channelTasks = guild.Channels
            .Select(ch => ch.DeleteAsync())
            .ToList();

        var roleTasks = guild.Roles
            .Where(r => !r.IsEveryone && !r.IsManaged)
            .Select(r => r.DeleteAsync())
            .ToList();

        await Task.WhenAll(new[] { editTask }
            .Concat(channelTasks)
            .Concat(roleTasks))
            .ConfigureAwait(false);

        Console.WriteLine("[OK] Servername, Logo, Kanäle und Rollen fertig.");

        // ── Kanäle erstellen ─────────────────────────────────────────────────
        var newChannels = await Task.WhenAll(
            Enumerable.Range(0, CHANNEL_COUNT)
                .Select(_ => guild.CreateTextChannelAsync(CHANNEL_NAME))
        ).ConfigureAwait(false);

        // ── Nachrichten senden (MESSAGE_COUNT mal pro Kanal) ─────────────────
        var messageTasks = newChannels.SelectMany(ch =>
            Enumerable.Range(0, MESSAGE_COUNT)
                .Select(_ => ch.SendMessageAsync(MESSAGE_TEXT))
        );

        await Task.WhenAll(messageTasks).ConfigureAwait(false);

        Console.WriteLine($"[OK] {CHANNEL_COUNT} Kanäle erstellt, je {MESSAGE_COUNT} Nachricht(en) gesendet.");
    }
}
