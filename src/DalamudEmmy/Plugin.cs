using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using DalamudEmmy.Chat;
using DalamudEmmy.Conversation;
using DalamudEmmy.Groups;
using DalamudEmmy.Memory;
using DalamudEmmy.People;
using DalamudEmmy.Persistence;
using DalamudEmmy.Provider;
using DalamudEmmy.Reply;
using DalamudEmmy.Sessions;
using DalamudEmmy.UI;
using System.Collections.Concurrent;
using DalamudEmmy.Companion;

namespace DalamudEmmy;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/emmy";

    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    internal static ICommandManager CommandManager { get; private set; } = null!;

    [PluginService]
    internal static IPluginLog Log { get; private set; } = null!;

    [PluginService]
    internal static IChatGui ChatGui { get; private set; } = null!;

    [PluginService]
    internal static IObjectTable ObjectTable { get; private set; } = null!;

    [PluginService]
    internal static IPartyList PartyList { get; private set; } = null!;

    [PluginService]
    internal static ITargetManager TargetManager { get; private set; } = null!;

    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IKeyState KeyState { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    private readonly CompanionBridge companion;
    private readonly Configuration configuration;
    private readonly PersistenceService persistence;
    private readonly ChatCaptureService chatCapture;
    private readonly ConversationContextService conversationContext;
    private readonly PeopleService people;
    private readonly GroupService groups;
    private readonly SessionService sessions;
    private readonly MemoryService memory;
    private readonly ReplyDecisionService replyDecision;
    private readonly LLMRequestService llmRequest;
    private readonly PluginWindow window;
    private readonly ConcurrentQueue<Action> mainThreadQueue = new();

    public Plugin()
    {
        var pluginVersion = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "unknown";

        this.configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.configuration.Initialize(PluginInterface);

        this.persistence = new PersistenceService(PluginInterface, Log);
        this.persistence.Initialize();

        this.people = new PeopleService(this.persistence);
        this.conversationContext = new ConversationContextService(this.configuration, this.people);
        this.groups = new GroupService(this.persistence);
        this.groups.Initialize();
        this.sessions = new SessionService(this.persistence);
        this.sessions.Initialize();
        this.memory = new MemoryService(this.persistence);
        this.memory.Initialize();
        this.llmRequest = new LLMRequestService(this.configuration, Log);
        this.replyDecision = new ReplyDecisionService(this.configuration, this.conversationContext, this.people, this.llmRequest, Log);
        this.chatCapture = new ChatCaptureService(ChatGui, ObjectTable, PartyList, TargetManager, this.configuration, this.people, this.persistence, this.replyDecision, this.llmRequest, this.EnqueueOnMainThread, Log);
        this.companion = new CompanionBridge(configuration,EnqueueOnMainThread);
        this.window = new PluginWindow(this.configuration, this.chatCapture, this.people);

        CommandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open the Dalamud Emmy window."
        });

        Framework.Update += this.OnFramework;
        PluginInterface.UiBuilder.Draw += this.DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi += this.OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += this.OpenMainUi;

        Log.Information("Dalamud Emmy loaded. Version={Version}", pluginVersion);
    }

    public void Dispose()
    {
        Framework.Update -= this.OnFramework;
        PluginInterface.UiBuilder.Draw -= this.DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi -= this.OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= this.OpenMainUi;

        CommandManager.RemoveHandler(CommandName);
        this.companion.Dispose();
        this.window.Dispose();
        this.chatCapture.Dispose();
        this.llmRequest.Dispose();
        this.persistence.Dispose();
    }

    private void OnCommand(string command, string arguments)
    {
        switch(arguments.Trim().ToLowerInvariant())
        {
            case "stop": case "pause": this.chatCapture.Stop(); this.companion.Stop(); break;
            case "resume": this.companion.SetMode(Emmy.Core.Mode.Assisted); break;
            case "web": this.companion.OpenWeb(); break;
            case "host": this.chatCapture.Stop(); this.configuration.UseCompanionHost=true; this.configuration.Save(); this.companion.IsOpen=true; break;
            case "legacy": this.companion.Stop(); this.configuration.UseCompanionHost=false; this.configuration.OperatingMode=OperatingMode.Assisted; this.configuration.Save(); break;
            default: this.companion.IsOpen=true; this.window.IsOpen=!configuration.UseCompanionHost; break;
        }
    }

    private void OnFramework(IFramework framework)
    {
        ProcessMainThreadQueue();
        companion.Tick();
    }

    private void DrawUi()
    {
        this.companion.Draw();
        if(!configuration.UseCompanionHost) this.window.Draw();
    }

    private void EnqueueOnMainThread(Action action)
    {
        if (action == null)
        {
            return;
        }

        this.mainThreadQueue.Enqueue(action);
    }

    private void ProcessMainThreadQueue()
    {
        for(var i=0;i<64 && this.mainThreadQueue.TryDequeue(out var action);i++)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Main-thread queued action failed");
            }
        }
    }

    private void OpenConfigUi()
    {
        this.companion.IsOpen = true;
        this.window.IsOpen = !configuration.UseCompanionHost;
    }

    private void OpenMainUi()
    {
        this.companion.IsOpen = true;
        this.window.IsOpen = !configuration.UseCompanionHost;
    }
}
