using Verse;

namespace RemoteTech;

public class GameComponent_TickDelayScheduler : GameComponent
{
    public DistributedTickScheduler distScheduler;
    public TickDelayScheduler scheduler;

    public GameComponent_TickDelayScheduler(Game game)
    {
    }

    public override void GameComponentTick()
    {
        if (scheduler == null)
        {
            InitTDS(); // Initialize scheduler if it’s null
        }

        if (distScheduler == null)
        {
            InitDTS(); // Initialize scheduler if it’s null
        }

        var ticksGame = Find.TickManager.TicksGame;
        scheduler?.Tick(ticksGame);
        distScheduler?.Tick(ticksGame);
        //base.GameComponentTick();
    }

    private void InitTDS()
    {
        if (scheduler != null)
        {
            return;
        }

        scheduler = new TickDelayScheduler();
        var ticks = Find.TickManager.TicksGame;
        //Log.Message($"Initializing scheduler at ticks: {ticks}");
        scheduler.Initialize(ticks);
        //Log.Message($"Last processed tick: {scheduler.lastProcessedTick}");
    }

    private void InitDTS()
    {
        if (distScheduler != null)
        {
            return;
        }

        distScheduler = new DistributedTickScheduler();
        var ticks = Find.TickManager.TicksGame;
        //Log.Message($"Initializing scheduler at ticks: {ticks}");
        distScheduler.Initialize(ticks);
        //Log.Message($"Last processed tick: {scheduler.lastProcessedTick}");
    }

    public override void StartedNewGame()
    {
        //Log.Message("StartedNewGame called");
        InitTDS();
        InitDTS();
    }

    public override void LoadedGame()
    {
        //Log.Message("LoadedGame called");
        InitTDS();
        InitDTS();
    }
}