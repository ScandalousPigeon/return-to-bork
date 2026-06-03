public class RTBPlayer
{
    public bool Learning = true;
    public Random random;
    public string MapType;
    public string ChallengeType;

    private Dictionary<string, RTBLocation> knownLocations = new();
    private Queue<string> plannedMoves = new();

    public RTBPlayer(string mapType, string challengeType)
    {
        MapType = mapType;
        ChallengeType = challengeType;
        random = new Random();
    }

    public void SetChallenge(string start, string egress)
    {
        // later calculate a path from start to egress using BFS
    }

    public (string, string) Action(RTBLocation location)
    {
        // Always remember the current room
        knownLocations[location.Name] = location;

        if (Learning)
        {
            return LearningAction(location);
        }

        return ChallengeAction(location);
    }

    private (string, string) LearningAction(RTBLocation location)
    {
        // temporary starter approach
        // wander around for a while, then enter challenge mode

        if (knownLocations.Count >= 100)
        {
            Learning = false;
            return ("challenge", "");
        }

        int exitIndex = random.Next(location.Exits.Count);
        string exitName = location.Exits[exitIndex].Name;

        return ("go", exitName);
    }

    private (string, string) ChallengeAction(RTBLocation location)
    {
        if (plannedMoves.Count > 0)
        {
            return ("go", plannedMoves.Dequeue());
        }

        // temporary fallback: random movement
        int exitIndex = random.Next(location.Exits.Count);
        string exitName = location.Exits[exitIndex].Name;

        return ("go", exitName);
    }
}