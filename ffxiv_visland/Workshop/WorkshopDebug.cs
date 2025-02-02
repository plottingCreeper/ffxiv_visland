﻿using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.MJI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using ImGuiNET;
using Lumina.Data;
using Lumina.Excel.GeneratedSheets2;
using System.Collections.Generic;
using System.Linq;
using visland.Helpers;

namespace visland.Workshop;

public unsafe class WorkshopDebug
{
    private WorkshopSolver _solver = new();
    private UITree _tree = new();
    private WorkshopSolver.FavorState _favorState = new();
    private WorkshopSolverFavorSheet? _favorSolution;
    private string[] _itemNames;

    public WorkshopDebug()
    {
        _itemNames = Service.LuminaGameData.GetExcelSheet<MJICraftworksObject>()!.Select(o => o.Item.Value?.Name ?? "").ToArray();
    }

    public void Draw()
    {
        if (ImGui.Button("Clear"))
            WorkshopUtils.ClearCurrentCycleSchedule();

        var ad = AgentMJICraftSchedule.Instance()->Data;
        var sheet = Service.LuminaGameData.GetExcelSheet<MJICraftworksObject>(Language.English)!;
        foreach (var na in _tree.Node($"Agent data: {(nint)ad:X}", ad == null))
        {
            _tree.LeafNode($"init={ad->UpdateState}, cur-cycle={ad->CycleDisplayed}");
            _tree.LeafNode($"setting addon={ad->OpenedModalAddonHandle}, ws={ad->CurScheduleSettingWorkshop}, slot={ad->CurScheduleSettingStartingSlot}, item=#{ad->CurScheduleSettingCraftIndex}, numMats={ad->CurScheduleSettingNumIngredients}");
            _tree.LeafNode($"rest mask={ad->RestCycles:X}, in-progress={ad->CycleInProgress}");
            int i = 0;
            foreach (ref var w in ad->WorkshopDataSpan)
            {
                foreach (var n in _tree.Node($"Workshop {i++}: {w.NumScheduleEntries} entries, {w.UsedTimeSlots:X} used", w.NumScheduleEntries == 0))
                {
                    for (int j = 0; j < w.NumScheduleEntries; ++j)
                    {
                        ref var e = ref w.EntryDataSpan[j];
                        _tree.LeafNode($"Item {j}: {e.CraftObjectId} ({sheet.GetRow(e.CraftObjectId)?.Item.Value?.Name}), startslot={e.StartingSlot}, dur={e.Duration}, started={e.Started}, efficient={e.Efficient}");
                    }
                }
            }

            foreach (var n in _tree.Node("Items", ad->Crafts.Size() == 0))
            {
                i = 0;
                foreach (ref readonly var item in ad->Crafts.Span)
                {
                    _tree.LeafNode($"Item {i++}: id={item.CraftObjectId} ({sheet.GetRow(item.CraftObjectId)?.Item.Value?.Name})");
                }
            }
        }

        var mji = MJIManager.Instance();
        _tree.LeafNode($"Popularity: dirty={mji->DemandDirty}, req={mji->RequestDemandType} obj={mji->RequestDemandCraftId}");
        if (!mji->DemandDirty)
        {
            DrawPopularity("Curr", mji->CurrentPopularity);
            DrawPopularity("Next", mji->NextPopularity);
        }

        var favorsData = mji->FavorState;
        var dataAvail = favorsData != null ? favorsData->UpdateState : -1;
        foreach (var nf in _tree.Node($"Favors: avail={dataAvail}", dataAvail != 2))
        {
            DrawFavorSetup(0, 4, 8);
            DrawFavorSetup(1, 6, 6);
            DrawFavorSetup(2, 8, 8);
            Utils.TextV("Init from game week:");
            ImGui.SameLine();
            if (ImGui.Button("Fetch demand"))
                WorkshopUtils.RequestDemand();
            ImGui.SameLine();
            if (ImGui.Button("Prev"))
                InitFavorsFromGame(0, -1);
            using (ImRaii.Disabled(mji->DemandDirty))
            {
                ImGui.SameLine();
                if (ImGui.Button("This"))
                    InitFavorsFromGame(3, mji->CurrentPopularity);
                ImGui.SameLine();
                if (ImGui.Button("Next"))
                    InitFavorsFromGame(6, mji->NextPopularity);
            }

            if (ImGui.Button("Solve!"))
                _favorSolution = new(_favorState);

            if (_favorSolution != null)
            {
                _tree.LeafNode($"Plan: {_favorSolution.Plan}");
                foreach (var n in _tree.Node("Links"))
                {
                    DrawLinked(_favorSolution.Favors[0], 4, _favorSolution.Links[0][0]);
                    DrawLinked(_favorSolution.Favors[0], 6, _favorSolution.Links[0][1]);
                    DrawLinked(_favorSolution.Favors[0], 8, _favorSolution.Links[0][2]);
                    DrawLinked(_favorSolution.Favors[1], 4, _favorSolution.Links[1][0]);
                    DrawLinked(_favorSolution.Favors[1], 6, _favorSolution.Links[1][1]);
                    DrawLinked(_favorSolution.Favors[1], 8, _favorSolution.Links[1][2]);
                    DrawLinked(_favorSolution.Favors[2], 4, _favorSolution.Links[2][0]);
                    DrawLinked(_favorSolution.Favors[2], 6, _favorSolution.Links[2][1]);
                    DrawLinked(_favorSolution.Favors[2], 8, _favorSolution.Links[2][2]);
                }
                foreach (var n in _tree.Node($"Solution ({_favorSolution.Recs.Count} cycles)", _favorSolution.Recs.Count == 0))
                {
                    int i = 0;
                    foreach (var r in _tree.Nodes(_favorSolution.Recs, r => new($"Schedule {i++}")))
                    {
                        _tree.LeafNodes(r.Slots, s => $"{s.Slot}: {s.CraftObjectId} '{sheet.GetRow(s.CraftObjectId)?.Item.Value?.Name}'");
                    }
                }
            }
        }
    }

    private void DrawPopularity(string tag, byte index)
    {
        var sheetCraft = Service.LuminaGameData.GetExcelSheet<MJICraftworksObject>()!;
        var pop = Service.LuminaRow<MJICraftworksPopularity>(index)!;
        foreach (var np in _tree.Node($"{tag} popularity={index}"))
        {
            _tree.LeafNodes(sheetCraft.Where(o => o.RowId > 0), o => $"{o.RowId} '{o.Item.Value?.Name}' = {pop.Popularity[o.RowId].Value?.Ratio}");
        }
    }

    private void DrawFavorSetup(int idx, int duration, int req)
    {
        var sheet = Service.LuminaGameData.GetExcelSheet<MJICraftworksObject>()!;
        Utils.TextV($"{duration}h:");
        ImGui.SameLine();
        UICombo.UInt($"###c{idx}", _itemNames, ref _favorState.CraftObjectIds[idx], i => i != 0 && sheet.GetRow(i)?.CraftingTime == duration);
        ImGui.SameLine();
        ImGui.DragInt($"###r{idx}", ref _favorState.CompletedCounts[idx], 0.03f, 0, req);
    }

    private void InitFavorsFromGame(int offset, int pop)
    {
        var state = MJIManager.Instance()->FavorState;
        for (int i = 0; i < 3; ++i)
        {
            _favorState.CraftObjectIds[i] = state->CraftObjectIds[i + offset];
            _favorState.CompletedCounts[i] = state->NumDelivered[i + offset] + state->NumScheduled[i + offset];
        }
        if (pop >= 0)
        {
            _favorState.Popularity.Set((uint)pop);
        }
    }

    private void DrawLinked(MJICraftworksObject obj, int duration, List<MJICraftworksObject> links)
    {
        foreach (var n in _tree.Node($"{duration}h linked to {obj.CraftingTime}h favor ({obj.Theme[0].Value?.Name}/{obj.Theme[1].Value?.Name})", links.Count == 0))
            _tree.Nodes(links, o => new($"{o.RowId} '{o.Item.Value?.Name}' {o.Theme[0].Value?.Name}/{o.Theme[1].Value?.Name} == {o.Value * _favorState.Popularity.Multiplier(o.RowId):f1}", true, _favorSolution!.Favors.Contains(o) ? 0xff00ff00 : 0xffffffff)).Count();
    }
}
