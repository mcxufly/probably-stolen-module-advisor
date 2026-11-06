using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using ModuleAdvisor;

static class TemporalChecks
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static TestItem Item(string id, int p, params string[] effects)
    {
        var item = new TestItem(id);
        item.modifiedShape = new GridShapeBuilder(1,1).SetDataFill(1);
        item.itemTypes.Add("MODULE");
        Set(item, ModuleHelper.MODULE_TAG, 0);
        Set(item, ModuleHelper.BONUS_PERCENTAGE_PERFORMANCE_INT, p);
        for (int i=0; i<effects.Length; i++) item.modifiedState.dict[i==0 ? ModuleEffectHelper.MODULE_EFFECT1_TAG : ModuleEffectHelper.MODULE_EFFECT2_TAG] =
            new TagState("effect").Enable().SetString(effects[i]);
        return item;
    }
    private static void Set(GameItem item,string key,int value) => item.modifiedState.dict[key]=new TagState(key).Enable().SetInt(value);
    private static GameGridInventory Grid(params GameItem[] items)
    {
        var grid=(GameGridInventory)RuntimeHelpers.GetUninitializedObject(typeof(GameGridInventory));
        typeof(GameGridInventory).GetField("items",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(grid,items.ToList());
        typeof(GameGridInventory).GetField("<inventoryShape>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic)!
            .SetValue(grid,new GridShapeBuilder(7,5));
        return grid;
    }
    internal static void Run()
    {
        var machine = Item("security_alarm",0);
        Set(machine,"MACHINERY_DRAWN_POWER_BASE",20);
        var learning = Item("learning",0,ModuleEffectHelper.MODULE_EFFECT_LEARNING_ALGORITHM);
        // Real native GetInt still consumes stored caps even when the tag is disabled.
        learning.modifiedState.dict[ModuleEffectHelper.LEARNING_ALGO_MAX_PERFORMANCE_INT] =
            new TagState("cap").SetInt(20);
        var plain = Item("plain",10);
        var a = new Placement {Module=0,Bay=0,Cells=new[]{0}};
        var b = new Placement {Module=1,Bay=0,Cells=new[]{0}};
        var options = new[]{a,b};
        var evaluation = new GameEvaluation(new[]{new Bay{Machine=machine,Grid=Grid()}},new[]{learning,plain},options,0);
        var learned = evaluation.Forecast(new[]{a},30,64);
        var steady = evaluation.Forecast(new[]{b},30,64);
        Check(learned.Mean.Last()==410 && steady.Mean.Last()==300,"Disabled persisted cap or alarm after-action integration wrong.");
        Check(learned.Samples==1 && learned.Lower.Last()==410,"Deterministic forecast should be exact.");
        var best = TemporalSearch.Improve(options,new Plan {Placements=new List<Placement>{b}},x=>evaluation.Forecast(x,30).Score(1),100);
        Check(best.Placements.Count==1 && best.Placements[0].Module==0,"Long-term refinement failed to beat instant module.");
        Check(evaluation.Forecast(Array.Empty<Placement>(),30).Mean.All(v=>v==0),"No-addition forecast baseline must be zero.");
        Check(learning.modifiedState.dict[ModuleHelper.BONUS_PERCENTAGE_PERFORMANCE_INT].GetInt()==0,"Forecast mutated live base.");
        Check(learning.modifiedState.dict[ModuleEffectHelper.LEARNING_ALGO_MAX_PERFORMANCE_INT].GetInt()==20,"Forecast mutated cap.");

        var unstable = Item("overvolted",20,ModuleEffectHelper.MODULE_EFFECT_OVERVOLTED);
        var risk = new GameEvaluation(new[]{new Bay{Machine=machine,Grid=Grid()}},new[]{unstable},new[]{a},0).Forecast(new[]{a},30,64);
        Check(Math.Abs(risk.AnyFailureProbability-(1-Math.Pow(.9,30)))<1e-12,"Analytic failure risk wrong.");
        Check(risk.Samples==64 && risk.Lower.Last()<=risk.Mean.Last(),"Stochastic sample/P10 aggregation wrong.");
        var repeated = evaluation.Forecast(new[]{a},30,64);
        Check(repeated.Mean.SequenceEqual(learned.Mean),"Forecast cache changed deterministic path.");
        var deal=Item("deal_maker",0); Set(deal,"MACHINERY_DRAWN_POWER_BASE",20);
        var dealResult = new GameEvaluation(new[]{new Bay{Machine=deal,Grid=Grid()}},new[]{unstable},new[]{a},0).Forecast(new[]{a},30,64);
        Check(dealResult.AnyFailureProbability==0 && dealResult.Samples==1,"Deal maker must not activate temporal effects.");
        int evaluations = 0;
        TemporalSearch.Improve(options, new Plan {Placements = new List<Placement>{b}}, x => {evaluations++; return x.Count;}, 24);
        Check(evaluations <= 24, "Temporal refinement exceeded hard budget including cleanup.");
        Console.WriteLine("Temporal GameEvaluation integration passed: raw persisted caps, horizon selection, paired baseline, analytic risk and non-aging deal maker.");
    }
}
