using Emmy.Core;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Security.Cryptography;
using System.Text;

namespace DalamudEmmy.Companion;

/// <summary>Native menu support is opt-in, with fresh signature and user-confirmed option selection.</summary>
public sealed unsafe class MenuAdapter : IDisposable
{
    private readonly IGameGui gui;
    private readonly IAddonLifecycle lifecycle;
    private long incarnation;
    public MenuAdapter(IGameGui gui,IAddonLifecycle lifecycle)
    {
        this.gui=gui;this.lifecycle=lifecycle;
        foreach(var name in new[]{"SelectString","SelectYesno"})
        {lifecycle.RegisterListener(AddonEvent.PostSetup,name,Changed);lifecycle.RegisterListener(AddonEvent.PreFinalize,name,Changed);}
    }
    private void Changed(AddonEvent type,AddonArgs args)=>incarnation++;
    public MenuState? Read()
    {
        var select=(AddonSelectString*)gui.GetAddonByName("SelectString",1).Address;
        if(select!=null&&select->IsVisible&&select->IsReady)
        {
            var count=select->PopupMenu.EntryCount;
            if(count is <=0 or >32 || select->PopupMenu.EntryNames == null || select->PopupMenu.List == null)return null;
            var options=new List<MenuOption>();
            for(var i=0;i<count;i++)options.Add(new(i,select->PopupMenu.EntryNames[i].ToString(),!select->PopupMenu.List->GetItemDisabledState(i)));
            return State("SelectString","",options);
        }
        var yesno=(AddonSelectYesno*)gui.GetAddonByName("SelectYesno",1).Address;
        if(yesno!=null&&yesno->IsVisible&&yesno->IsReady&&yesno->AtkValuesCount>=4)
        {
            var options=new List<MenuOption>();
            var a=yesno->StandardTypedAtkValues;
            options.Add(new(0,a->Button1Text.String.ToString(),yesno->YesButton!=null&&yesno->YesButton->IsEnabled));
            options.Add(new(1,a->Button2Text.String.ToString(),yesno->NoButton!=null&&yesno->NoButton->IsEnabled));
            // Three-button and hold-to-confirm dialogs need their own tested semantics.
            if(!string.IsNullOrEmpty(a->Button3Text.String.ToString()))return null;
            return State("SelectYesno",a->PromptText.String.ToString(),options);
        }
        return null;
    }
    private MenuState State(string addon,string prompt,List<MenuOption> options)
    {
        var signature=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{incarnation}:{addon}:{prompt}:"+string.Join("|",options.Select(o=>$"{o.Index}:{o.Text}:{o.Enabled}")))));
        return new(addon,signature,prompt,options.ToArray());
    }
    public bool Select(ActionRequest request)
    {
        var current=Read();
        if(!request.Confirmed||current is null||current.Signature!=request.MenuSignature||!current.Options.Any(o=>o.Index==request.Option&&o.Enabled))return false;
        var addon=(AtkUnitBase*)gui.GetAddonByName(current.Addon,1).Address;
        if(addon==null||!addon->IsReady||!addon->IsVisible)return false;
        addon->FireCallbackInt(request.Option);return true;
    }
    public void Dispose()
    {
        foreach(var name in new[]{"SelectString","SelectYesno"})
        {lifecycle.UnregisterListener(AddonEvent.PostSetup,name,Changed);lifecycle.UnregisterListener(AddonEvent.PreFinalize,name,Changed);}
    }
}
