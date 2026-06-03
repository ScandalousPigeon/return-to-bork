
public class ReturnToBork
{
    public RTBMap Map { get; set; }
    public RTBPlayer Player { get; set; }

    public Location PlayerLocation { get; set; }
    public bool ChallengeMode { get; set; }

    public int Coins;
    public int CoinsStart;
    public int Rope;
    public int RopeStart;
    public int Actions;
    public int ActionLimit;
    public HashSet<string> Keys;
    public HashSet<string> OpenedDoorExits;
    public HashSet<string> TreasureObtainedLocations;
    public HashSet<string> BridgeBuiltExits;

    public ReturnToBork(RTBMap map, RTBPlayer player, int coinsStart, int ropeStart, int actionLimit)
    {
        Map = map;
        Player = player;
        PlayerLocation = Map.LearningModeStart;
        ChallengeMode = false;
        Keys = new HashSet<string>();
        OpenedDoorExits = new HashSet<string>();
        TreasureObtainedLocations = new HashSet<string>();
        BridgeBuiltExits = new HashSet<string>();
        Actions = 0;
        Coins = coinsStart;
        Rope = ropeStart;
        CoinsStart = coinsStart;
        RopeStart = ropeStart;    
        ActionLimit = actionLimit;
    }

    private void Go(string e)
    {
        if (!PlayerLocation.Exits.ContainsKey(e))
        {
            throw new InvalidOperationException($"Tried to go through a non-existent exit: {e}"); 
        }

        Exit E = PlayerLocation.Exits[e];
        if (ChallengeMode)
        {
            if (E.LockColour != null && !OpenedDoorExits.Contains(e))
            {
                if (!Keys.Contains(E.LockColour))
                    throw new InvalidOperationException($"Tried to go through a locked door without the '{E.LockColour}' key.");
                Keys.Remove(E.LockColour);
                OpenedDoorExits.Add(e);
                OpenedDoorExits.Add(E.ReverseExitName);
            }

            if (E.Toll > Coins)
            {
                throw new InvalidOperationException("Tried to go over a Troll bridge without enough coins.");
            }
            Coins -= E.Toll;

            if (!BridgeBuiltExits.Contains(e))
            {
                if (E.RopeCost > Rope)
                    throw new InvalidOperationException("Tried to build a rope bridge without enough rope.");
                if (E.RopeCost > 0)
                {
                    Rope -= E.RopeCost;
                    BridgeBuiltExits.Add(e);
                    BridgeBuiltExits.Add(E.ReverseExitName);
                }
            }
        }

        PlayerLocation = E.Destination;
    }

    public void Reset()
    {
        if (ChallengeMode) { throw new InvalidOperationException("Attempted to reset while in challenge mode."); }
        PlayerLocation = Map.LearningModeStart;
    }

    private void EnterChallengeMode()
    {
        if (ChallengeMode) { throw new InvalidOperationException("Attempted to enter challenge mode while already in challenge mode."); }
        PlayerLocation = Map.Start;
        ChallengeMode = true;
        Player.SetChallenge(Map.Start.Name, Map.Egress.Name);
    }

    private void ParseCommand(string command, string e)
    {
        switch (command)
        {
            case "go":
                Go(e);
                break;
            case "reset":
                Reset();
                break;
            case "challenge":
                EnterChallengeMode();
                break;
            default:
                throw new InvalidOperationException($"Command not recognised: {command}.");
        }
    }

    public void Learn()
    {
        while (!ChallengeMode) {
            var (command, e) = Player.Action(PlayerLocation.ToRTBLocation());
            ParseCommand(command, e);
        }
    }

    public bool Challenge() {
        string command, e;
        // Timeout will be enforced externally
        while (true) {
            if (PlayerLocation.Treasure)
            {
                TreasureObtainedLocations.Add(PlayerLocation.Name);
            }

            // Pickup of keys is automatic and free upon entering a location, so we add it to the
            // player's inventory before checking the action limit or egress condition.
            if (PlayerLocation.KeyColour != null)
            {
                Keys.Add(PlayerLocation.KeyColour);
            }

            Actions++;
            if (Actions > ActionLimit)
            {
                throw new InvalidOperationException($"The action limit was exceeded. Actions: {Actions}, Limit: {ActionLimit}");
            }
            if (PlayerLocation == Map.Egress && Map.TreasureLocations.Count == TreasureObtainedLocations.Count) { 
                Console.WriteLine($"Arrived at Egress using {Actions} actions, {CoinsStart - Coins} coins, and {RopeStart - Rope} rope.");
                return true;
            }

            (command, e) = Player.Action(PlayerLocation.ToRTBLocation());
            ParseCommand(command, e);       
        }
    }
}