using System;
using System.Collections.Generic;

public class RTBPlayer
{
    public bool Learning = true;
    public Random random;
    public string MapType;
    public string ChallengeType;

    private MazeSolver mazeSolver;

    public RTBPlayer(string mapType, string challengeType)
    {
        MapType = mapType;
        ChallengeType = challengeType;
        random = new Random();

        mazeSolver = new MazeSolver(challengeType);
    }

    public void SetChallenge(string start, string egress)
    {
        Learning = false;

        if (MapType == "Maze")
        {
            mazeSolver.PrepareChallengeRoute(start, egress);
        }
    }

    public (string, string) Action(RTBLocation location)
    {
        if (MapType == "Maze")
        {
            var action = mazeSolver.ChooseNextAction(location);

            if (action.Item1 == "challenge")
            {
                Learning = false;
            }

            return action;
        }

        // Fallback for non-maze maps.
        // You can replace this later with KeysSolver, TrollSolver, etc.
        if (Learning)
        {
            Learning = false;
            return ("challenge", "");
        }

        int exitIndex = random.Next(location.Exits.Count);
        return ("go", location.Exits[exitIndex].Name);
    }
}


public class MazeSolver
{
    private bool learning = true;

    private Dictionary<string, Dictionary<string, string?>> graph;
    private HashSet<string> treasureLocations;
    private Queue<(string command, string exit)> plannedActions;

    private string challengeType;

    private string? learningStart;
    private string? previousLocation;
    private string? previousExit;
    private bool previousActionWasGo;

    public MazeSolver(string challengeType)
    {
        this.challengeType = challengeType;

        graph = new Dictionary<string, Dictionary<string, string?>>();
        treasureLocations = new HashSet<string>();
        plannedActions = new Queue<(string command, string exit)>();

        learningStart = null;
        previousLocation = null;
        previousExit = null;
        previousActionWasGo = false;
    }

    public void PrepareChallengeRoute(string start, string egress)
    {
        learning = false;
        plannedActions.Clear();

        if (challengeType.StartsWith("100"))
        {
            QueueTreasureRouteThenEgress(start, egress);
        }
        else
        {
            QueueShortestRoute(start, egress);
        }
    }

    public (string, string) ChooseNextAction(RTBLocation location)
    {
        RecordLocationAndExits(location);

        if (plannedActions.Count > 0)
        {
            return ReturnActionAndRememberExit(plannedActions.Dequeue(), location.Name);
        }

        if (learning)
        {
            return ChooseLearningAction(location);
        }

        return ("challenge", "");
    }

    private void RecordLocationAndExits(RTBLocation location)
    {
        if (learningStart == null)
        {
            learningStart = location.Name;
        }

        AddKnownExitsFromLocation(location);

        if (location.Treasure)
        {
            treasureLocations.Add(location.Name);
        }

        if (previousActionWasGo && previousLocation != null && previousExit != null)
        {
            graph[previousLocation][previousExit] = location.Name;
        }

        previousActionWasGo = false;
    }

    private void AddKnownExitsFromLocation(RTBLocation location)
    {
        if (!graph.ContainsKey(location.Name))
        {
            graph[location.Name] = new Dictionary<string, string?>();
        }

        foreach (RTBExit exit in location.Exits)
        {
            if (!graph[location.Name].ContainsKey(exit.Name))
            {
                graph[location.Name][exit.Name] = null;
            }
        }
    }

    private (string, string) ChooseLearningAction(RTBLocation location)
    {
        var unknownExit = FindExitWithUnknownDestination();

        if (unknownExit == null)
        {
            learning = false;
            return ("challenge", "");
        }

        string targetLocation = unknownExit.Value.location;
        string exitToExplore = unknownExit.Value.exit;

        List<string>? routeToTarget = FindShortestExitSequence(location.Name, targetLocation);

        if (routeToTarget == null)
        {
            return ("reset", "");
        }

        foreach (string exit in routeToTarget)
        {
            plannedActions.Enqueue(("go", exit));
        }

        plannedActions.Enqueue(("go", exitToExplore));
        plannedActions.Enqueue(("reset", ""));

        return ReturnActionAndRememberExit(plannedActions.Dequeue(), location.Name);
    }

    private (string location, string exit)? FindExitWithUnknownDestination()
    {
        foreach (var locationEntry in graph)
        {
            string location = locationEntry.Key;
            Dictionary<string, string?> exits = locationEntry.Value;

            foreach (var exitEntry in exits)
            {
                string exit = exitEntry.Key;
                string? destination = exitEntry.Value;

                if (destination == null)
                {
                    return (location, exit);
                }
            }
        }

        return null;
    }

    private void QueueShortestRoute(string start, string goal)
    {
        List<string>? route = FindShortestExitSequence(start, goal);

        if (route == null)
        {
            return;
        }

        foreach (string exit in route)
        {
            plannedActions.Enqueue(("go", exit));
        }
    }

    private void QueueTreasureRouteThenEgress(string start, string egress)
    {
        string currentLocation = start;
        HashSet<string> remainingTreasures = new HashSet<string>(treasureLocations);

        remainingTreasures.Remove(currentLocation);

        while (remainingTreasures.Count > 0)
        {
            string? nearestTreasure = null;
            List<string>? shortestTreasureRoute = null;

            foreach (string treasure in remainingTreasures)
            {
                List<string>? route = FindShortestExitSequence(currentLocation, treasure);

                if (route == null)
                {
                    continue;
                }

                if (shortestTreasureRoute == null || route.Count < shortestTreasureRoute.Count)
                {
                    shortestTreasureRoute = route;
                    nearestTreasure = treasure;
                }
            }

            if (nearestTreasure == null || shortestTreasureRoute == null)
            {
                break;
            }

            foreach (string exit in shortestTreasureRoute)
            {
                plannedActions.Enqueue(("go", exit));
            }

            currentLocation = nearestTreasure;
            remainingTreasures.Remove(nearestTreasure);
        }

        QueueShortestRoute(currentLocation, egress);
    }

    private List<string>? FindShortestExitSequence(string start, string goal)
    {
        if (start == goal)
        {
            return new List<string>();
        }

        Queue<string> queue = new Queue<string>();
        HashSet<string> visited = new HashSet<string>();
        Dictionary<string, (string previousLocation, string exitUsed)> previousSteps =
            new Dictionary<string, (string previousLocation, string exitUsed)>();

        queue.Enqueue(start);
        visited.Add(start);

        while (queue.Count > 0)
        {
            string currentLocation = queue.Dequeue();

            if (!graph.ContainsKey(currentLocation))
            {
                continue;
            }

            foreach (var exitEntry in graph[currentLocation])
            {
                string exit = exitEntry.Key;
                string? destination = exitEntry.Value;

                if (destination == null)
                {
                    continue;
                }

                if (visited.Contains(destination))
                {
                    continue;
                }

                visited.Add(destination);
                previousSteps[destination] = (currentLocation, exit);

                if (destination == goal)
                {
                    return BuildExitSequence(start, goal, previousSteps);
                }

                queue.Enqueue(destination);
            }
        }

        return null;
    }

    private List<string> BuildExitSequence(
        string start,
        string goal,
        Dictionary<string, (string previousLocation, string exitUsed)> previousSteps)
    {
        List<string> reversedExitSequence = new List<string>();
        string currentLocation = goal;

        while (currentLocation != start)
        {
            var step = previousSteps[currentLocation];

            reversedExitSequence.Add(step.exitUsed);
            currentLocation = step.previousLocation;
        }

        reversedExitSequence.Reverse();
        return reversedExitSequence;
    }

    private (string, string) ReturnActionAndRememberExit(
        (string command, string exit) action,
        string currentLocation)
    {
        if (action.command == "go")
        {
            previousLocation = currentLocation;
            previousExit = action.exit;
            previousActionWasGo = true;
        }
        else
        {
            previousActionWasGo = false;
        }

        return action;
    }
}