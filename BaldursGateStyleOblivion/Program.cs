using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Oblivion;

namespace BaldursGateStyleOblivion
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<IOblivionMod, IOblivionModGetter>(RunPatch)
                .SetTypicalOpen(GameRelease.Oblivion, "BaldursGateStyleOblivion.esp")
                .Run(args);
        }

        public static void RunPatch(IPatcherState<IOblivionMod, IOblivionModGetter> state)
        {
            Console.WriteLine($"Loaded {state.LoadOrder.Count} plugins.");
            ActorReports.Write(state);
        }
    }
}

