using Discord;
using Discord.WebSocket;

class Program
{
    private static DiscordSocketClient _client = null!;

    // Logo einmalig beim Start laden
    private static byte[]? _logoBytes;

    static async Task Main()
    {
        var token = Environment.GetEnvironmentVariable("DISCORD_TOKEN");
        if (string.IsNullOrEmpty(token))
        {
            Console.Error.WriteLine("[FEHLER] DISCORD_TOKEN ist nicht gesetzt!");
            return;
        }

        // Logo vorladen
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
                           | GatewayIntents.MessageContent   // Pflicht für Prefix-Commands
        };

        _client = new DiscordSocketClient(config);

        _client.Log            += LogAsync;
        _client.Ready          += ReadyAsync;
        _client.MessageReceived += MessageHandler;   // Prefix-Command statt Slash
        _client.ButtonExecuted += ButtonHandler;

        await _client.LoginAsync(TokenType.Bot, token).ConfigureAwait(false);
        await _client.StartAsync().ConfigureAwait(false);
        await Task.Delay(-1).ConfigureAwait(false);
    }

    // ── Logging ───────────────────────────────────────────────────────────────
    private static Task LogAsync(LogMessage log)
    {
        Console.WriteLine(log.ToString());
        return Task.CompletedTask;
    }

    // ── Bot bereit ────────────────────────────────────────────────────────────
    private static Task ReadyAsync()
    {
        Console.WriteLine("[OK] Bot ist online – !clean-myserver bereit.");
        return Task.CompletedTask;
    }

    // ── !clean-myserver aufgerufen ────────────────────────────────────────────
    private static async Task MessageHandler(SocketMessage message)
    {
        // Bots und andere Channels ignorieren
        if (message is not SocketUserMessage userMsg) return;
        if (userMsg.Author.IsBot) return;
        if (userMsg.Channel is not SocketGuildChannel guildChannel) return;

        // Command prüfen
        if (!userMsg.Content.Equals("!clean-myserver", StringComparison.OrdinalIgnoreCase)) return;

        // Nur Admins dürfen den Command nutzen
        var guildUser = guildChannel.Guild.GetUser(userMsg.Author.Id);
        if (!guildUser.GuildPermissions.Administrator)
        {
            await userMsg.Channel.SendMessageAsync(
                "❌ Du brauchst Administrator-Rechte für diesen Command."
            ).ConfigureAwait(false);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle("⚠️  WARNUNG — Server wird geleert!")
            .WithDescription(
                "**Diese Aktion löscht ALLES auf diesem Server!**\n\n" +
                "📋 **Was passiert:**\n" +
                "▸ Alle Kanäle werden gelöscht\n" +
                "▸ Alle Rollen werden gelöscht\n" +
                "▸ Servername → **SERVER GEREINIGT**\n" +
                "▸ Server-Logo wird aktualisiert\n" +
                "▸ 3 **#information** Kanäle werden erstellt\n\n" +
                "⛔ **Nicht rückgängig machbar!**"
            )
            .WithColor(Color.Red)
            .WithFooter(f => f.Text = "Clean Your Server • Nur für Admins")
            .Build();

        var components = new ComponentBuilder()
            .WithButton("Ja, Server leeren!", "cys_confirm", ButtonStyle.Danger)
            .WithButton("Abbrechen",          "cys_cancel",  ButtonStyle.Secondary)
            .Build();

        await userMsg.Channel.SendMessageAsync(embed: embed, components: components)
                             .ConfigureAwait(false);
    }

    // ── Button-Klick ──────────────────────────────────────────────────────────
    private static async Task ButtonHandler(SocketMessageComponent component)
    {
        // Nur Admins dürfen bestätigen
        if (component.Channel is SocketGuildChannel gc)
        {
            var user = gc.Guild.GetUser(component.User.Id);
            if (!user.GuildPermissions.Administrator)
            {
                await component.RespondAsync("❌ Nur Admins können dies bestätigen.", ephemeral: true)
                               .ConfigureAwait(false);
                return;
            }
        }

        // Abbrechen
        if (component.Data.CustomId == "cys_cancel")
        {
            var cancelEmbed = new EmbedBuilder()
                .WithTitle("❌ Abgebrochen")
                .WithDescription("Keine Änderungen vorgenommen.")
                .WithColor(Color.LightGrey)
                .Build();

            await component.UpdateAsync(msg =>
            {
                msg.Embed      = cancelEmbed;
                msg.Components = new ComponentBuilder().Build();
            }).ConfigureAwait(false);
            return;
        }

        if (component.Data.CustomId != "cys_confirm") return;

        // Sofort-Rückmeldung
        var waitEmbed = new EmbedBuilder()
            .WithTitle("🧹 Reinigung läuft...")
            .WithDescription("Alles wird parallel gelöscht. Bitte warten.")
            .WithColor(new Color(0xFFAA00))
            .Build();

        await component.UpdateAsync(msg =>
        {
            msg.Embed      = waitEmbed;
            msg.Components = new ComponentBuilder().Build();
        }).ConfigureAwait(false);

        if (component.Channel is not SocketGuildChannel guildChannel) return;
        var guild = guildChannel.Guild;

        // ── ALLE OPERATIONEN GLEICHZEITIG STARTEN ────────────────────────────

        // 1. Guild-Edit (Name + Logo)
        var editTask = guild.ModifyAsync(p =>
        {
            p.Name = "SERVER GEREINIGT";            // ← Servername hier ändern
            if (_logoBytes is not null)
                p.Icon = new Image(new MemoryStream(_logoBytes));
        });

        // 2. Alle Kanäle löschen
        var channelTasks = guild.Channels
            .Select(ch => ch.DeleteAsync())
            .ToList();

        // 3. Alle Rollen löschen
        var roleTasks = guild.Roles
            .Where(r => !r.IsEveryone && !r.IsManaged)
            .Select(r => r.DeleteAsync())
            .ToList();

        // Alles gleichzeitig abwarten
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
