using System;
using System.Collections.Generic;
using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using WhatsInTheGrass.Compatibility;
using WhatsInTheGrass.Patches;

namespace WhatsInTheGrass;

internal sealed class ModEntry : Mod
{
  private const string LauncherDrawerDictAssetName = "aedenthorn.LauncherDrawer/dict";

  internal static ModConfig Config { get; private set; } = new();
  internal static IMonitor MonitorObject { get; private set; } = null!;

  public override void Entry(IModHelper helper)
  {
    MonitorObject = Monitor;
    Config = helper.ReadConfig<ModConfig>();
    I18n.Init(helper.Translation);

    GrassFadePatch.Initialize(
      new Harmony(ModManifest.UniqueID),
      helper.ModRegistry.IsLoaded(ModCompat.MoreGrass)
    );

    helper.Events.GameLoop.GameLaunched += OnGameLaunched;
    helper.Events.Content.AssetRequested += OnAssetRequested;
    helper.Events.GameLoop.ReturnedToTitle += (_, _) => GrassFadePatch.ClearCache();
  }

  private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
  {
    // Gated on GMCM since the action opens our GMCM page.
    if (
      !e.NameWithoutLocale.IsEquivalentTo(LauncherDrawerDictAssetName)
      || !Helper.ModRegistry.IsLoaded(ModCompat.LauncherDrawer)
      || !Helper.ModRegistry.IsLoaded(ModCompat.Gmcm)
    )
    {
      return;
    }

    e.Edit(data =>
    {
      IDictionary<string, Dictionary<string, object>> dict = data.AsDictionary<
        string,
        Dictionary<string, object>
      >().Data;
      dict[ModManifest.UniqueID] = new Dictionary<string, object>
      {
        { "Name", I18n.LauncherDrawer_EntryName() },
        { "Description", I18n.LauncherDrawer_EntryDescription() },
        {
          "Action",
          new Action(() =>
            Helper
              .ModRegistry.GetApi<IGenericModConfigMenuApi>(ModCompat.Gmcm)
              ?.OpenModMenuAsChildMenu(ModManifest)
          )
        },
      };
    });
  }

  private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
  {
    IGenericModConfigMenuApi? gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>(
      ModCompat.Gmcm
    );
    if (gmcm == null)
    {
      return;
    }

    gmcm.Register(ModManifest, () => Config = new ModConfig(), () => Helper.WriteConfig(Config));
    gmcm.AddBoolOption(
      ModManifest,
      () => Config.FadeOverForage,
      v => Config.FadeOverForage = v,
      () => I18n.Config_FadeOverForage_Name(),
      () => I18n.Config_FadeOverForage_Tooltip()
    );
    AddOpacityOption(
      gmcm,
      () => Config.ForageOpacityPercent,
      v => Config.ForageOpacityPercent = v,
      () => I18n.Config_ForageOpacity_Tooltip()
    );
    gmcm.AddBoolOption(
      ModManifest,
      () => Config.FadeOverAnimals,
      v => Config.FadeOverAnimals = v,
      () => I18n.Config_FadeOverAnimals_Name(),
      () => I18n.Config_FadeOverAnimals_Tooltip()
    );
    AddOpacityOption(
      gmcm,
      () => Config.AnimalOpacityPercent,
      v => Config.AnimalOpacityPercent = v,
      () => I18n.Config_AnimalOpacity_Tooltip()
    );
  }

  private void AddOpacityOption(
    IGenericModConfigMenuApi gmcm,
    Func<int> getValue,
    Action<int> setValue,
    Func<string> tooltip
  )
  {
    gmcm.AddNumberOption(
      ModManifest,
      getValue,
      setValue,
      () => I18n.Config_GrassOpacity_Name(),
      tooltip,
      min: 0,
      max: 100,
      interval: 5,
      formatValue: v => $"{v}%"
    );
  }
}
