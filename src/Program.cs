using Discord;
using Discord.WebSocket;

class Program
{
    private static DiscordSocketClient _client = null!;
    private static byte[]? _logoBytes;

    // ── Hier alles anpassen ───────────────────────────────────────────────────
    const string SERVER_NAME   = "SERVER GEREINIGT"; // Servername nach dem Clean
    const string CHANNEL_NAME  = "information";       // Name der neuen Kanäle
    const int    CHANNEL_COUNT = 3;                   // Anzahl der Kanäle
    const int    MESSAGE_COUNT = 1;                   // Nachrichten pro Kanal
    const string MESSAGE_TEXT  =
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

    // Hilfsmethode: Fehler bei einem einzelnen Task ignorieren, nicht alles abbrechen
    private static async Task TryRun(Task t, string label)
    {
        try
        {
            await t.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SKIP] {label}: {ex.Message}");
        }
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
        Console.WriteLine("[START] Reinigung gestartet...");

        // ── 1. Servername + Logo (parallel, sofort) ───────────────────────────
        await TryRun(guild.ModifyAsync(p =>
        {
            p.Name = SERVER_NAME;
            if (_logoBytes is not null)
                p.Icon = new Image(new MemoryStream(_logoBytes));
        }), "Server-Edit").ConfigureAwait(false);

        Console.WriteLine("[OK] Servername/Logo gesetzt.");

        // ── 2. Kanäle löschen (alle parallel, Fehler einzeln ignorieren) ──────
        var channelTasks = guild.Channels
            .Select(ch => TryRun(ch.DeleteAsync(), $"Kanal {ch.Name}"))
            .ToList();

        await Task.WhenAll(channelTasks).ConfigureAwait(false);
        Console.WriteLine("[OK] Alle Kanäle gelöscht.");

        // ── 3. Rollen löschen ─────────────────────────────────────────────────
        // Nur Rollen löschen die UNTER der höchsten Bot-Rolle liegen
        int botTopPosition = guild.CurrentUser.Roles
            .DefaultIfEmpty()
            .Max(r => r?.Position ?? 0);

        var rolesToDelete = guild.Roles
            .Where(r => !r.IsEveryone && !r.IsManaged && r.Position < botTopPosition)
            .ToList();

        Console.WriteLine($"[INFO] {rolesToDelete.Count} Rollen werden gelöscht...");

        var roleTasks = rolesToDelete
            .Select(r => TryRun(r.DeleteAsync(), $"Rolle {r.Name}"))
            .ToList();

        await Task.WhenAll(roleTasks).ConfigureAwait(false);
        Console.WriteLine("[OK] Rollen gelöscht.");

        // ── 4. Neue Kanäle erstellen ──────────────────────────────────────────
        var newChannels = await Task.WhenAll(
            Enumerable.Range(0, CHANNEL_COUNT)
                .Select(_ => guild.CreateTextChannelAsync(CHANNEL_NAME))
        ).ConfigureAwait(false);

        Console.WriteLine($"[OK] {CHANNEL_COUNT} Kanäle erstellt.");

        // ── 5. Nachrichten senden ─────────────────────────────────────────────
        var messageTasks = newChannels.SelectMany(ch =>
            Enumerable.Range(0, MESSAGE_COUNT)
                .Select(_ => TryRun(ch.SendMessageAsync(MESSAGE_TEXT), $"Nachricht in {ch.Name}"))
        );

        await Task.WhenAll(messageTasks).ConfigureAwait(false);
        Console.WriteLine($"[FERTIG] Je {MESSAGE_COUNT} Nachricht(en) pro Kanal gesendet.");
    }
}
