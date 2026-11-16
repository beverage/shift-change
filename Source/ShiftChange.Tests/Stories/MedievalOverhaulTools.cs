using System.Collections;
using DevTools.Testing;
using static ShiftChange.Tests.GameDefs;
using static ShiftChange.Tests.ToolKit;
using static ShiftChange.Tests.WeaponKit;

namespace ShiftChange.Tests
{
    /// <summary>
    /// Medieval Overhaul's tools through a stand, its pickaxe standing in for the rest: they are
    /// ordinary weapons to a stand, so a second tool would run the same path again. A hauler
    /// stocks a new stand with it, and it goes to work and back. The tool is found by
    /// defName, so nothing about the mod reaches the shipping dll or this one's references. The
    /// fixture registers only with Medieval Overhaul on the list; with it on, a renamed pickaxe
    /// fails both tests.
    /// </summary>
    [TestFixture(TestType.Playing), ExecutionPriority(RunOrder.First - (int)CaseGroup.MedievalOverhaulTools)]
    [LoadIfModsActive(MedievalOverhaulPackageId)]
    internal class MedievalOverhaulTools : StoryFixture
    {
        internal const string Pickaxe = "DankPyon_MeleeWeapon_Pickaxe";

        [Test]
        [TestDescription("a hauler stocks a new stand with Medieval Overhaul's pickaxe")]
        internal IEnumerator AHaulerStocksANewStandWithAPickaxe()
        {
            return Staged(ArmedOnly, ArmedOnlySays, (scene, story) => HaulerStocks(story, scene, Pickaxe));
        }

        [Test]
        [TestDescription("Medieval Overhaul's pickaxe from a stand goes to work and back")]
        internal IEnumerator ThePickaxeGoesToWorkAndBack()
        {
            return Staged(ToolStand, ToolStandSays,
                          (scene, story) => AcceptTool(story, scene, Pickaxe, "Mine", StatNamed("MiningSpeed"), MineJob));
        }
    }
}
