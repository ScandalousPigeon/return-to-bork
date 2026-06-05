using System;

public class RTBPlayer
{
    public bool Learning = true;
    public Random random;
    public string MapType;
    public string ChallengeType;
    public RTBPlayer(string mapType, string challengeType)
    {
        MapType = mapType;             // `Maze`, `Keys`, `Trolls`, or `Ropes`
        ChallengeType = challengeType; // `A`, `AS`, `AC`, `100`, `100S` or `100C`
        random = new Random();         // used for random walk
    }

    // Called after entering challenge mode to give you the start and egress
    public void SetChallenge(string start, string egress)
    {
        // Here is a good place to calculate your sequence of commands
        // to get from the start to the egress, obeying the rules of the challenge
    }

    public (string, string) Action(RTBLocation location)
    {
        // return a command
        
        // You probably want some logic to dispatch to separate functions 
        // for learning mode and different map/challenge types
        
        // The following is a very simple random walk with no learning phase.
        // It will solve some challenges, but not very quickly!
        
        // Enter challenge mode immediately
        if (Learning) { 
            Learning = false;
            return ("challenge", "");
        }
    
        // Follow a randomly chosen exit
        int exitIndex = random.Next(location.Exits.Count);
        string e = location.Exits[exitIndex].Name;
        
        return ("go", e);
    }
}