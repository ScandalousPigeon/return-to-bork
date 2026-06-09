using System;
using System.Collections.Generic;

public class RTBPlayer
{
    public bool Learning = true;
    public Random random;
    public string MapType;
    public string ChallengeType;

    private MazeSolver mazeSolver;
    private TrollsAnyPercentChallenge trollsAnyPercent;
    private Trolls100PercentChallenge trolls100Percent;
    private KeysAnyPercentChallenge keysAnyPercent;
    private Ropes100PercentChallenge ropes100Percent;

    public RTBPlayer(string mapType, string challengeType)
    {
        MapType = mapType;
        ChallengeType = challengeType;
        random = new Random();

        mazeSolver = new MazeSolver(challengeType);
        trollsAnyPercent = new TrollsAnyPercentChallenge();
        trolls100Percent = new Trolls100PercentChallenge();
        keysAnyPercent = new KeysAnyPercentChallenge(challengeType);
        ropes100Percent = new Ropes100PercentChallenge();
    }

    public void SetChallenge(string start, string egress)
    {
        Learning = false;

        if (MapType == "Maze")
        {
            mazeSolver.PrepareChallengeRoute(start, egress);
        }
        else if (MapType == "Trolls" && IsAnyPercentChallenge())
        {
            trollsAnyPercent.PrepareChallengeRoute(start, egress);
        }
        else if (MapType == "Trolls" && IsHundredPercentChallenge())
        {
            trolls100Percent.PrepareChallengeRoute(start, egress);
        }
        else if (MapType == "Keys" && IsAnyPercentChallenge())
        {
            keysAnyPercent.PrepareChallengeRoute(start, egress);
        }
        else if (MapType == "Ropes" && IsHundredPercentChallenge())
        {
            ropes100Percent.PrepareChallengeRoute(start, egress);
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

        if (MapType == "Trolls" && IsAnyPercentChallenge())
        {
            var action = trollsAnyPercent.ChooseNextAction(location);

            if (action.Item1 == "challenge")
            {
                Learning = false;
            }

            return action;
        }

        if (MapType == "Trolls" && IsHundredPercentChallenge())
        {
            var action = trolls100Percent.ChooseNextAction(location);

            if (action.Item1 == "challenge")
            {
                Learning = false;
            }

            return action;
        }

        if (MapType == "Keys" && IsAnyPercentChallenge())
        {
            var action = keysAnyPercent.ChooseNextAction(location);

            if (action.Item1 == "challenge")
            {
                Learning = false;
            }

            return action;
        }

        if (MapType == "Ropes" && IsHundredPercentChallenge())
        {
            var action = ropes100Percent.ChooseNextAction(location);

            if (action.Item1 == "challenge")
            {
                Learning = false;
            }

            return action;
        }

        // Fallback for map/challenge types that you have not implemented yet.
        if (Learning)
        {
            Learning = false;
            return ("challenge", "");
        }

        int exitIndex = random.Next(location.Exits.Count);
        return ("go", location.Exits[exitIndex].Name);
    }

    private bool IsAnyPercentChallenge()
    {
        return ChallengeType == "A" || ChallengeType == "AS" || ChallengeType == "AC";
    }

    private bool IsHundredPercentChallenge()
    {
        return ChallengeType == "100" || ChallengeType == "100S" || ChallengeType == "100C";
    }
}

/// <summary>
/// Simple BFS implementation
/// </summary>
public class MazeSolver
{
    private const long ExactStateLimit = 5000000;
    private const int MaximumExactTreasureCount = 20;
    private const int UnreachableDistance = 1000000000;

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
        previousActionWasGo = false;

        if (IsHundredPercentChallenge())
        {
            QueueCollectAllTreasuresRoute(start, egress);
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

        // This should normally only happen if the challenge has already been completed.
        return ("challenge", "");
    }

    private bool IsHundredPercentChallenge()
    {
        return challengeType.StartsWith("100");
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
        EnsureLocationExists(location.Name);

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

    /// <summary>
    /// Maze 100% planner.
    ///
    /// For small/medium treasure counts, this uses a state-space BFS over
    /// (location, collectedTreasureMask). Since every maze move has unit cost,
    /// the first time BFS reaches (egress, allTreasures) is the true shortest
    /// 100% route.
    ///
    /// If the treasure state space would be too large, it falls back to a fast
    /// cheapest-insertion route over shortest-path distances. That fallback is
    /// not guaranteed optimal, but is much stronger than nearest-treasure greedy
    /// and protects the 100C tests from exponential blow-ups.
    /// </summary>
    private void QueueCollectAllTreasuresRoute(string start, string egress)
    {
        List<string>? route;

        if (CanUseExactCollectAllSearch())
        {
            route = FindShortestCollectAllTreasuresRoute(start, egress);
        }
        else
        {
            route = FindCheapestInsertionTreasureRoute(start, egress);
        }

        if (route == null)
        {
            // Connected maze maps should not need this, but it avoids returning
            // a second "challenge" command if something unexpected happens.
            route = FindShortestExitSequence(start, egress);
        }

        if (route == null)
        {
            return;
        }

        foreach (string exit in route)
        {
            plannedActions.Enqueue(("go", exit));
        }
    }

    private bool CanUseExactCollectAllSearch()
    {
        int treasureCount = treasureLocations.Count;

        if (treasureCount > MaximumExactTreasureCount)
        {
            return false;
        }

        long estimatedStates = ((long)graph.Count) * (1L << treasureCount);
        return estimatedStates <= ExactStateLimit;
    }

    private List<string>? FindShortestCollectAllTreasuresRoute(string start, string egress)
    {
        List<string> treasures = new List<string>(treasureLocations);
        Dictionary<string, int> treasureIndex = new Dictionary<string, int>();

        for (int i = 0; i < treasures.Count; i++)
        {
            treasureIndex[treasures[i]] = i;
        }

        int startMask = 0;
        if (treasureIndex.ContainsKey(start))
        {
            startMask |= 1 << treasureIndex[start];
        }

        int goalMask = (1 << treasures.Count) - 1;
        var startState = (location: start, mask: startMask);

        Queue<(string location, int mask)> queue = new Queue<(string location, int mask)>();
        HashSet<(string location, int mask)> visited = new HashSet<(string location, int mask)>();
        Dictionary<(string location, int mask), PreviousMazeState> previous =
            new Dictionary<(string location, int mask), PreviousMazeState>();

        queue.Enqueue(startState);
        visited.Add(startState);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (current.location == egress && current.mask == goalMask)
            {
                return BuildStateSpaceExitSequence(startState, current, previous);
            }

            if (!graph.ContainsKey(current.location))
            {
                continue;
            }

            foreach (var exitEntry in graph[current.location])
            {
                string exit = exitEntry.Key;
                string? destination = exitEntry.Value;

                if (destination == null)
                {
                    continue;
                }

                int nextMask = current.mask;
                if (treasureIndex.ContainsKey(destination))
                {
                    nextMask |= 1 << treasureIndex[destination];
                }

                var nextState = (location: destination, mask: nextMask);
                if (visited.Contains(nextState))
                {
                    continue;
                }

                visited.Add(nextState);
                previous[nextState] = new PreviousMazeState(
                    current.location,
                    current.mask,
                    exit
                );
                queue.Enqueue(nextState);
            }
        }

        return null;
    }

    private List<string> BuildStateSpaceExitSequence(
        (string location, int mask) startState,
        (string location, int mask) goalState,
        Dictionary<(string location, int mask), PreviousMazeState> previous)
    {
        List<string> reversedExitSequence = new List<string>();
        var current = goalState;

        while (!current.Equals(startState))
        {
            PreviousMazeState step = previous[current];
            reversedExitSequence.Add(step.ExitUsed);
            current = (location: step.PreviousLocation, mask: step.PreviousMask);
        }

        reversedExitSequence.Reverse();
        return reversedExitSequence;
    }

    private List<string>? FindCheapestInsertionTreasureRoute(string start, string egress)
    {
        List<string> remainingTreasures = new List<string>();
        foreach (string treasure in treasureLocations)
        {
            // The start treasure is picked up before the first challenge action.
            // The egress treasure, if present, is picked up on the final arrival.
            if (treasure != start && treasure != egress)
            {
                remainingTreasures.Add(treasure);
            }
        }

        Dictionary<string, ShortestPathTree> shortestPaths = BuildShortestPathTrees(
            start,
            remainingTreasures
        );

        List<string> visitOrder = new List<string>();

        while (remainingTreasures.Count > 0)
        {
            int bestTreasureIndex = -1;
            int bestInsertPosition = 0;
            int bestAddedDistance = int.MaxValue;

            for (int treasureIndex = 0; treasureIndex < remainingTreasures.Count; treasureIndex++)
            {
                string treasure = remainingTreasures[treasureIndex];

                for (int insertPosition = 0; insertPosition <= visitOrder.Count; insertPosition++)
                {
                    string previousLocation = insertPosition == 0
                        ? start
                        : visitOrder[insertPosition - 1];
                    string nextLocation = insertPosition == visitOrder.Count
                        ? egress
                        : visitOrder[insertPosition];

                    int addedDistance = Distance(shortestPaths, previousLocation, treasure)
                        + Distance(shortestPaths, treasure, nextLocation)
                        - Distance(shortestPaths, previousLocation, nextLocation);

                    if (addedDistance < bestAddedDistance)
                    {
                        bestAddedDistance = addedDistance;
                        bestTreasureIndex = treasureIndex;
                        bestInsertPosition = insertPosition;
                    }
                }
            }

            if (bestTreasureIndex < 0)
            {
                return null;
            }

            string chosenTreasure = remainingTreasures[bestTreasureIndex];
            remainingTreasures.RemoveAt(bestTreasureIndex);
            visitOrder.Insert(bestInsertPosition, chosenTreasure);
        }

        return BuildRouteFromVisitOrder(start, egress, visitOrder);
    }

    private Dictionary<string, ShortestPathTree> BuildShortestPathTrees(
        string start,
        List<string> treasureSources)
    {
        Dictionary<string, ShortestPathTree> shortestPaths = new Dictionary<string, ShortestPathTree>();

        shortestPaths[start] = RunBreadthFirstSearch(start);

        foreach (string treasure in treasureSources)
        {
            if (!shortestPaths.ContainsKey(treasure))
            {
                shortestPaths[treasure] = RunBreadthFirstSearch(treasure);
            }
        }

        return shortestPaths;
    }

    private int Distance(
        Dictionary<string, ShortestPathTree> shortestPaths,
        string start,
        string goal)
    {
        if (!shortestPaths.ContainsKey(start))
        {
            shortestPaths[start] = RunBreadthFirstSearch(start);
        }

        int distance;
        if (shortestPaths[start].TryGetDistance(goal, out distance))
        {
            return distance;
        }

        return UnreachableDistance;
    }

    private List<string>? BuildRouteFromVisitOrder(
        string start,
        string egress,
        List<string> visitOrder)
    {
        List<string> fullRoute = new List<string>();
        HashSet<string> alreadyCollected = new HashSet<string>();
        string currentLocation = start;

        if (treasureLocations.Contains(start))
        {
            alreadyCollected.Add(start);
        }

        List<string> waypoints = new List<string>(visitOrder);
        waypoints.Add(egress);

        foreach (string waypoint in waypoints)
        {
            bool isFinalEgress = waypoint == egress;

            if (!isFinalEgress && alreadyCollected.Contains(waypoint))
            {
                continue;
            }

            List<string>? routeSegment = FindShortestExitSequence(currentLocation, waypoint);
            if (routeSegment == null)
            {
                return null;
            }

            foreach (string exit in routeSegment)
            {
                fullRoute.Add(exit);

                string? nextLocation = FindKnownDestination(currentLocation, exit);
                if (nextLocation == null)
                {
                    return null;
                }

                currentLocation = nextLocation;
                if (treasureLocations.Contains(currentLocation))
                {
                    alreadyCollected.Add(currentLocation);
                }
            }
        }

        return fullRoute;
    }

    private List<string>? FindShortestExitSequence(string start, string goal)
    {
        return RunBreadthFirstSearch(start).BuildExitSequence(goal);
    }

    private ShortestPathTree RunBreadthFirstSearch(string start)
    {
        Queue<string> queue = new Queue<string>();
        Dictionary<string, int> distances = new Dictionary<string, int>();
        Dictionary<string, PreviousStep> previousSteps = new Dictionary<string, PreviousStep>();

        queue.Enqueue(start);
        distances[start] = 0;

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

                if (destination == null || distances.ContainsKey(destination))
                {
                    continue;
                }

                distances[destination] = distances[currentLocation] + 1;
                previousSteps[destination] = new PreviousStep(currentLocation, exit);
                queue.Enqueue(destination);
            }
        }

        return new ShortestPathTree(start, distances, previousSteps);
    }

    private string? FindKnownDestination(string location, string exit)
    {
        if (!graph.ContainsKey(location) || !graph[location].ContainsKey(exit))
        {
            return null;
        }

        return graph[location][exit];
    }

    private void EnsureLocationExists(string location)
    {
        if (!graph.ContainsKey(location))
        {
            graph[location] = new Dictionary<string, string?>();
        }
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

    private readonly struct PreviousMazeState
    {
        public readonly string PreviousLocation;
        public readonly int PreviousMask;
        public readonly string ExitUsed;

        public PreviousMazeState(string previousLocation, int previousMask, string exitUsed)
        {
            PreviousLocation = previousLocation;
            PreviousMask = previousMask;
            ExitUsed = exitUsed;
        }
    }

    private readonly struct PreviousStep
    {
        public readonly string PreviousLocation;
        public readonly string ExitUsed;

        public PreviousStep(string previousLocation, string exitUsed)
        {
            PreviousLocation = previousLocation;
            ExitUsed = exitUsed;
        }
    }

    private class ShortestPathTree
    {
        private string start;
        private Dictionary<string, int> distances;
        private Dictionary<string, PreviousStep> previousSteps;

        public ShortestPathTree(
            string start,
            Dictionary<string, int> distances,
            Dictionary<string, PreviousStep> previousSteps)
        {
            this.start = start;
            this.distances = distances;
            this.previousSteps = previousSteps;
        }

        public bool TryGetDistance(string goal, out int distance)
        {
            return distances.TryGetValue(goal, out distance);
        }

        public List<string>? BuildExitSequence(string goal)
        {
            if (!distances.ContainsKey(goal))
            {
                return null;
            }

            List<string> reversedExitSequence = new List<string>();
            string currentLocation = goal;

            while (currentLocation != start)
            {
                PreviousStep step = previousSteps[currentLocation];
                reversedExitSequence.Add(step.ExitUsed);
                currentLocation = step.PreviousLocation;
            }

            reversedExitSequence.Reverse();
            return reversedExitSequence;
        }
    }
}

/// <summary>
/// Solves Trolls Any% by learning the full map, then finding a route from start
/// to egress that minimises total troll toll. Ties are broken by fewer moves.
/// Uses Dijkstra's Algorithm.
/// </summary>
public class TrollsAnyPercentChallenge
{
    private bool learning = true;

    private Dictionary<string, Dictionary<string, LearnedTrollExit>> graph;
    private Queue<(string command, string exit)> learningActions;
    private Queue<(string command, string exit)> challengeActions;

    private string? learningStart;
    private string? previousLocation;
    private string? previousExit;
    private int previousToll;
    private bool previousActionWasGo;

    public TrollsAnyPercentChallenge()
    {
        graph = new Dictionary<string, Dictionary<string, LearnedTrollExit>>();
        learningActions = new Queue<(string command, string exit)>();
        challengeActions = new Queue<(string command, string exit)>();

        learningStart = null;
        previousLocation = null;
        previousExit = null;
        previousToll = 0;
        previousActionWasGo = false;
    }

    public (string, string) ChooseNextAction(RTBLocation location)
    {
        RecordLocationAndExits(location);

        if (learning)
        {
            if (learningActions.Count > 0)
            {
                return ReturnActionAndRememberExit(learningActions.Dequeue(), location);
            }

            return ChooseLearningAction(location);
        }

        if (challengeActions.Count > 0)
        {
            return ReturnActionAndRememberExit(challengeActions.Dequeue(), location);
        }

        // Usually the engine checks completion before asking for another action.
        return ("challenge", "");
    }

    public void PrepareChallengeRoute(string start, string egress)
    {
        learning = false;
        challengeActions.Clear();

        List<string> route = FindLowestTollRoute(start, egress);

        foreach (string exit in route)
        {
            challengeActions.Enqueue(("go", exit));
        }
    }

    private void RecordLocationAndExits(RTBLocation location)
    {
        if (learningStart == null)
        {
            learningStart = location.Name;
        }

        AddKnownExitsFromLocation(location);

        if (previousActionWasGo && previousLocation != null && previousExit != null)
        {
            EnsureLocationExists(previousLocation);

            if (!graph[previousLocation].ContainsKey(previousExit))
            {
                graph[previousLocation][previousExit] =
                    new LearnedTrollExit(previousExit, previousToll, location.Name);
            }
            else
            {
                graph[previousLocation][previousExit].Toll = previousToll;
                graph[previousLocation][previousExit].Destination = location.Name;
            }
        }

        previousActionWasGo = false;
    }

    private void AddKnownExitsFromLocation(RTBLocation location)
    {
        EnsureLocationExists(location.Name);

        foreach (RTBExit exit in location.Exits)
        {
            if (!graph[location.Name].ContainsKey(exit.Name))
            {
                graph[location.Name][exit.Name] = new LearnedTrollExit(exit.Name, exit.Toll, null);
            }
            else
            {
                // The destination may still be unknown, but the toll is visible immediately.
                graph[location.Name][exit.Name].Toll = exit.Toll;
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

        List<string>? routeToTarget = FindShortestKnownExitSequence(location.Name, targetLocation);

        if (routeToTarget == null)
        {
            return ("reset", "");
        }

        foreach (string exit in routeToTarget)
        {
            learningActions.Enqueue(("go", exit));
        }

        learningActions.Enqueue(("go", exitToExplore));
        learningActions.Enqueue(("reset", ""));

        return ReturnActionAndRememberExit(learningActions.Dequeue(), location);
    }

    private (string location, string exit)? FindExitWithUnknownDestination()
    {
        foreach (var locationEntry in graph)
        {
            string location = locationEntry.Key;
            Dictionary<string, LearnedTrollExit> exits = locationEntry.Value;

            foreach (var exitEntry in exits)
            {
                string exit = exitEntry.Key;
                LearnedTrollExit exitInfo = exitEntry.Value;

                if (exitInfo.Destination == null)
                {
                    return (location, exit);
                }
            }
        }

        return null;
    }

    private List<string>? FindShortestKnownExitSequence(string start, string goal)
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
                string? destination = exitEntry.Value.Destination;

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

    private List<string> FindLowestTollRoute(string start, string egress)
    {
        if (start == egress)
        {
            return new List<string>();
        }

        var distance = new Dictionary<string, RouteCost>();
        var previous = new Dictionary<string, PreviousStep>();
        var queue = new PriorityQueue<string, RouteCost>();

        foreach (string location in graph.Keys)
        {
            distance[location] = RouteCost.Infinity;
        }

        distance[start] = new RouteCost(0, 0);
        queue.Enqueue(start, distance[start]);

        while (queue.Count > 0)
        {
            queue.TryDequeue(out string? current, out RouteCost currentCost);

            if (current == null)
            {
                continue;
            }

            if (!distance.ContainsKey(current) || !distance[current].Equals(currentCost))
            {
                continue;
            }

            if (current == egress)
            {
                break;
            }

            if (!graph.ContainsKey(current))
            {
                continue;
            }

            foreach (LearnedTrollExit exit in graph[current].Values)
            {
                if (exit.Destination == null)
                {
                    continue;
                }

                RouteCost newCost = new RouteCost(
                    currentCost.Coins + exit.Toll,
                    currentCost.Moves + 1
                );

                if (!distance.ContainsKey(exit.Destination) || newCost.CompareTo(distance[exit.Destination]) < 0)
                {
                    distance[exit.Destination] = newCost;
                    previous[exit.Destination] = new PreviousStep(current, exit.Name);
                    queue.Enqueue(exit.Destination, newCost);
                }
            }
        }

        if (!previous.ContainsKey(egress))
        {
            throw new InvalidOperationException(
                $"No Trolls Any% route found from {start} to {egress}. " +
                "The map may not have been fully learned."
            );
        }

        return BuildExitSequence(start, egress, previous);
    }

    private static List<string> BuildExitSequence(
        string start,
        string goal,
        Dictionary<string, PreviousStep> previousSteps)
    {
        List<string> reversedExitSequence = new List<string>();
        string currentLocation = goal;

        while (currentLocation != start)
        {
            PreviousStep step = previousSteps[currentLocation];
            reversedExitSequence.Add(step.ExitUsed);
            currentLocation = step.PreviousLocation;
        }

        reversedExitSequence.Reverse();
        return reversedExitSequence;
    }

    private static List<string> BuildExitSequence(
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
        RTBLocation currentLocation)
    {
        if (action.command == "go")
        {
            previousLocation = currentLocation.Name;
            previousExit = action.exit;
            previousToll = FindTollForExit(currentLocation, action.exit);
            previousActionWasGo = true;
        }
        else
        {
            previousActionWasGo = false;
        }

        return action;
    }

    private int FindTollForExit(RTBLocation location, string exitName)
    {
        foreach (RTBExit exit in location.Exits)
        {
            if (exit.Name == exitName)
            {
                return exit.Toll;
            }
        }

        throw new InvalidOperationException(
            $"Tried to remember toll for non-existent exit {exitName} from {location.Name}."
        );
    }

    private void EnsureLocationExists(string location)
    {
        if (!graph.ContainsKey(location))
        {
            graph[location] = new Dictionary<string, LearnedTrollExit>();
        }
    }

    private class LearnedTrollExit
    {
        public string Name;
        public int Toll;
        public string? Destination;

        public LearnedTrollExit(string name, int toll, string? destination)
        {
            Name = name;
            Toll = toll;
            Destination = destination;
        }
    }

    private readonly struct PreviousStep
    {
        public readonly string PreviousLocation;
        public readonly string ExitUsed;

        public PreviousStep(string previousLocation, string exitUsed)
        {
            PreviousLocation = previousLocation;
            ExitUsed = exitUsed;
        }
    }

    private readonly struct RouteCost : IComparable<RouteCost>, IEquatable<RouteCost>
    {
        public static readonly RouteCost Infinity = new RouteCost(int.MaxValue, int.MaxValue);

        public readonly int Coins;
        public readonly int Moves;

        public RouteCost(int coins, int moves)
        {
            Coins = coins;
            Moves = moves;
        }

        public int CompareTo(RouteCost other)
        {
            int coinCompare = Coins.CompareTo(other.Coins);
            if (coinCompare != 0)
            {
                return coinCompare;
            }

            return Moves.CompareTo(other.Moves);
        }

        public bool Equals(RouteCost other)
        {
            return Coins == other.Coins && Moves == other.Moves;
        }

        public override bool Equals(object? obj)
        {
            return obj is RouteCost other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Coins, Moves);
        }
    }
}

/// <summary>
/// Solves Trolls 100% by learning the full map, compressing toll-free locations
/// into areas, then planning an inter-area route that visits every treasure area
/// before finishing at the egress. The final location route collects all known
/// treasures inside each visited area before crossing the next toll bridge.
/// </summary>
public class Trolls100PercentChallenge
{
    private const long ExactAreaStateLimit = 8000000;
    private const int MaximumExactAreaCount = 18;
    private const int MaximumExactLocalTreasureCount = 14;
    private const int UnreachableDistance = 1000000000;

    private bool learning = true;

    private Dictionary<string, Dictionary<string, LearnedTrollExit>> graph;
    private HashSet<string> treasureLocations;
    private Queue<(string command, string exit)> learningActions;
    private Queue<(string command, string exit)> challengeActions;

    private string? learningStart;
    private string? previousLocation;
    private string? previousExit;
    private int previousToll;
    private bool previousActionWasGo;

    public Trolls100PercentChallenge()
    {
        graph = new Dictionary<string, Dictionary<string, LearnedTrollExit>>();
        treasureLocations = new HashSet<string>();
        learningActions = new Queue<(string command, string exit)>();
        challengeActions = new Queue<(string command, string exit)>();

        learningStart = null;
        previousLocation = null;
        previousExit = null;
        previousToll = 0;
        previousActionWasGo = false;
    }

    public (string, string) ChooseNextAction(RTBLocation location)
    {
        RecordLocationAndExits(location);

        if (learning)
        {
            if (learningActions.Count > 0)
            {
                return ReturnActionAndRememberExit(learningActions.Dequeue(), location);
            }

            return ChooseLearningAction(location);
        }

        if (challengeActions.Count > 0)
        {
            return ReturnActionAndRememberExit(challengeActions.Dequeue(), location);
        }

        // Usually the engine checks completion before asking for another action.
        return ("challenge", "");
    }

    public void PrepareChallengeRoute(string start, string egress)
    {
        learning = false;
        challengeActions.Clear();
        previousActionWasGo = false;

        List<string>? route = BuildTrolls100Route(start, egress);

        if (route == null)
        {
            // Fallback for unexpected malformed learned data. This will not be a
            // valid 100% completion if treasures remain, but avoids crashing the
            // player on ordinary completion maps.
            route = FindLowestTollLocationRoute(start, egress);
        }

        if (route == null)
        {
            return;
        }

        foreach (string exit in route)
        {
            challengeActions.Enqueue(("go", exit));
        }
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
            EnsureLocationExists(previousLocation);

            if (!graph[previousLocation].ContainsKey(previousExit))
            {
                graph[previousLocation][previousExit] =
                    new LearnedTrollExit(previousExit, previousToll, location.Name);
            }
            else
            {
                graph[previousLocation][previousExit].Toll = previousToll;
                graph[previousLocation][previousExit].Destination = location.Name;
            }
        }

        previousActionWasGo = false;
    }

    private void AddKnownExitsFromLocation(RTBLocation location)
    {
        EnsureLocationExists(location.Name);

        foreach (RTBExit exit in location.Exits)
        {
            if (!graph[location.Name].ContainsKey(exit.Name))
            {
                graph[location.Name][exit.Name] = new LearnedTrollExit(exit.Name, exit.Toll, null);
            }
            else
            {
                graph[location.Name][exit.Name].Toll = exit.Toll;
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

        List<string>? routeToTarget = FindShortestKnownExitSequence(location.Name, targetLocation);

        if (routeToTarget == null)
        {
            return ("reset", "");
        }

        foreach (string exit in routeToTarget)
        {
            learningActions.Enqueue(("go", exit));
        }

        learningActions.Enqueue(("go", exitToExplore));
        learningActions.Enqueue(("reset", ""));

        return ReturnActionAndRememberExit(learningActions.Dequeue(), location);
    }

    private (string location, string exit)? FindExitWithUnknownDestination()
    {
        foreach (var locationEntry in graph)
        {
            string location = locationEntry.Key;
            Dictionary<string, LearnedTrollExit> exits = locationEntry.Value;

            foreach (var exitEntry in exits)
            {
                string exit = exitEntry.Key;
                LearnedTrollExit exitInfo = exitEntry.Value;

                if (exitInfo.Destination == null)
                {
                    return (location, exit);
                }
            }
        }

        return null;
    }

    private List<string>? BuildTrolls100Route(string start, string egress)
    {
        if (treasureLocations.Count == 0)
        {
            return FindLowestTollLocationRoute(start, egress);
        }

        AreaModel model = BuildAreaModel();

        if (!model.LocationToArea.ContainsKey(start) || !model.LocationToArea.ContainsKey(egress))
        {
            return null;
        }

        int startArea = model.LocationToArea[start];
        int egressArea = model.LocationToArea[egress];
        HashSet<int> requiredAreas = FindTreasureAreas(model);

        List<AreaTransition>? areaWalk;
        if (CanUseExactAreaSearch(model.AreaCount, requiredAreas.Count))
        {
            areaWalk = FindLowestTollAreaWalk(model, startArea, egressArea, requiredAreas);
        }
        else
        {
            areaWalk = FindCheapestInsertionAreaWalk(model, startArea, egressArea, requiredAreas);
        }

        if (areaWalk == null)
        {
            return null;
        }

        return BuildLocationRouteFromAreaWalk(model, start, egress, areaWalk);
    }

    private bool CanUseExactAreaSearch(int areaCount, int requiredAreaCount)
    {
        if (requiredAreaCount > MaximumExactAreaCount)
        {
            return false;
        }

        long estimatedStates = ((long)areaCount) * (1L << requiredAreaCount);
        return estimatedStates <= ExactAreaStateLimit;
    }

    private AreaModel BuildAreaModel()
    {
        AreaModel model = new AreaModel();

        foreach (string location in graph.Keys)
        {
            if (model.LocationToArea.ContainsKey(location))
            {
                continue;
            }

            int areaId = model.Areas.Count;
            List<string> areaLocations = new List<string>();
            Queue<string> queue = new Queue<string>();

            model.Areas.Add(areaLocations);
            model.LocationToArea[location] = areaId;
            queue.Enqueue(location);

            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                areaLocations.Add(current);

                if (!graph.ContainsKey(current))
                {
                    continue;
                }

                foreach (LearnedTrollExit exit in graph[current].Values)
                {
                    if (exit.Destination == null || exit.Toll != 0)
                    {
                        continue;
                    }

                    if (model.LocationToArea.ContainsKey(exit.Destination))
                    {
                        continue;
                    }

                    model.LocationToArea[exit.Destination] = areaId;
                    queue.Enqueue(exit.Destination);
                }
            }
        }

        for (int i = 0; i < model.Areas.Count; i++)
        {
            model.Adjacency.Add(new List<AreaTransition>());
        }

        foreach (var locationEntry in graph)
        {
            string fromLocation = locationEntry.Key;

            if (!model.LocationToArea.ContainsKey(fromLocation))
            {
                continue;
            }

            int fromArea = model.LocationToArea[fromLocation];

            foreach (LearnedTrollExit exit in locationEntry.Value.Values)
            {
                if (exit.Destination == null || exit.Toll == 0)
                {
                    continue;
                }

                if (!model.LocationToArea.ContainsKey(exit.Destination))
                {
                    continue;
                }

                int toArea = model.LocationToArea[exit.Destination];
                if (fromArea == toArea)
                {
                    continue;
                }

                model.Adjacency[fromArea].Add(new AreaTransition(
                    fromArea,
                    toArea,
                    fromLocation,
                    exit.Name,
                    exit.Destination,
                    exit.Toll
                ));
            }
        }

        return model;
    }

    private HashSet<int> FindTreasureAreas(AreaModel model)
    {
        HashSet<int> requiredAreas = new HashSet<int>();

        foreach (string treasure in treasureLocations)
        {
            if (model.LocationToArea.ContainsKey(treasure))
            {
                requiredAreas.Add(model.LocationToArea[treasure]);
            }
        }

        return requiredAreas;
    }

    private List<AreaTransition>? FindLowestTollAreaWalk(
        AreaModel model,
        int startArea,
        int egressArea,
        HashSet<int> requiredAreas)
    {
        Dictionary<int, int> requiredIndex = new Dictionary<int, int>();
        int index = 0;
        foreach (int area in requiredAreas)
        {
            requiredIndex[area] = index++;
        }

        int startMask = 0;
        if (requiredIndex.ContainsKey(startArea))
        {
            startMask |= 1 << requiredIndex[startArea];
        }

        int goalMask = (1 << requiredIndex.Count) - 1;
        var startState = (area: startArea, mask: startMask);

        var distance = new Dictionary<(int area, int mask), AreaRouteCost>();
        var previous = new Dictionary<(int area, int mask), PreviousAreaState>();
        var queue = new PriorityQueue<(int area, int mask), AreaRouteCost>();

        distance[startState] = new AreaRouteCost(0, 0);
        queue.Enqueue(startState, distance[startState]);

        while (queue.Count > 0)
        {
            queue.TryDequeue(out var current, out AreaRouteCost currentCost);

            if (!distance.ContainsKey(current) || !distance[current].Equals(currentCost))
            {
                continue;
            }

            if (current.area == egressArea && current.mask == goalMask)
            {
                return BuildAreaTransitionSequence(startState, current, previous);
            }

            foreach (AreaTransition edge in model.Adjacency[current.area])
            {
                int nextMask = current.mask;
                if (requiredIndex.ContainsKey(edge.ToArea))
                {
                    nextMask |= 1 << requiredIndex[edge.ToArea];
                }

                var nextState = (area: edge.ToArea, mask: nextMask);
                AreaRouteCost newCost = new AreaRouteCost(
                    currentCost.Coins + edge.Toll,
                    currentCost.Crossings + 1
                );

                if (!distance.ContainsKey(nextState) || newCost.CompareTo(distance[nextState]) < 0)
                {
                    distance[nextState] = newCost;
                    previous[nextState] = new PreviousAreaState(current.area, current.mask, edge);
                    queue.Enqueue(nextState, newCost);
                }
            }
        }

        return null;
    }

    private List<AreaTransition> BuildAreaTransitionSequence(
        (int area, int mask) startState,
        (int area, int mask) goalState,
        Dictionary<(int area, int mask), PreviousAreaState> previous)
    {
        List<AreaTransition> reversed = new List<AreaTransition>();
        var current = goalState;

        while (!current.Equals(startState))
        {
            PreviousAreaState step = previous[current];
            reversed.Add(step.Transition);
            current = (area: step.PreviousArea, mask: step.PreviousMask);
        }

        reversed.Reverse();
        return reversed;
    }

    private List<AreaTransition>? FindCheapestInsertionAreaWalk(
        AreaModel model,
        int startArea,
        int egressArea,
        HashSet<int> requiredAreas)
    {
        List<int> remainingAreas = new List<int>();
        foreach (int area in requiredAreas)
        {
            if (area != startArea && area != egressArea)
            {
                remainingAreas.Add(area);
            }
        }

        Dictionary<int, AreaShortestPathTree> shortestPaths = new Dictionary<int, AreaShortestPathTree>();
        shortestPaths[startArea] = RunAreaDijkstra(model, startArea);

        List<int> visitOrder = new List<int>();

        while (remainingAreas.Count > 0)
        {
            int bestAreaIndex = -1;
            int bestInsertPosition = 0;
            long bestAddedCoins = long.MaxValue;
            long bestAddedCrossings = long.MaxValue;

            for (int areaIndex = 0; areaIndex < remainingAreas.Count; areaIndex++)
            {
                int candidate = remainingAreas[areaIndex];

                for (int insertPosition = 0; insertPosition <= visitOrder.Count; insertPosition++)
                {
                    int previousArea = insertPosition == 0
                        ? startArea
                        : visitOrder[insertPosition - 1];
                    int nextArea = insertPosition == visitOrder.Count
                        ? egressArea
                        : visitOrder[insertPosition];

                    AreaRouteCost previousToCandidate = AreaDistance(model, shortestPaths, previousArea, candidate);
                    AreaRouteCost candidateToNext = AreaDistance(model, shortestPaths, candidate, nextArea);
                    AreaRouteCost previousToNext = AreaDistance(model, shortestPaths, previousArea, nextArea);

                    if (previousToCandidate.IsInfinity || candidateToNext.IsInfinity)
                    {
                        continue;
                    }

                    long addedCoins = (long)previousToCandidate.Coins + candidateToNext.Coins;
                    long addedCrossings = (long)previousToCandidate.Crossings + candidateToNext.Crossings;

                    if (!previousToNext.IsInfinity)
                    {
                        addedCoins -= previousToNext.Coins;
                        addedCrossings -= previousToNext.Crossings;
                    }

                    if (addedCoins < bestAddedCoins ||
                        (addedCoins == bestAddedCoins && addedCrossings < bestAddedCrossings))
                    {
                        bestAddedCoins = addedCoins;
                        bestAddedCrossings = addedCrossings;
                        bestAreaIndex = areaIndex;
                        bestInsertPosition = insertPosition;
                    }
                }
            }

            if (bestAreaIndex < 0)
            {
                return null;
            }

            int chosenArea = remainingAreas[bestAreaIndex];
            remainingAreas.RemoveAt(bestAreaIndex);
            visitOrder.Insert(bestInsertPosition, chosenArea);
        }

        return BuildAreaWalkFromVisitOrder(model, shortestPaths, startArea, egressArea, visitOrder);
    }

    private List<AreaTransition>? BuildAreaWalkFromVisitOrder(
        AreaModel model,
        Dictionary<int, AreaShortestPathTree> shortestPaths,
        int startArea,
        int egressArea,
        List<int> visitOrder)
    {
        List<AreaTransition> areaWalk = new List<AreaTransition>();
        int currentArea = startArea;

        List<int> waypoints = new List<int>(visitOrder);
        waypoints.Add(egressArea);

        foreach (int waypoint in waypoints)
        {
            if (currentArea == waypoint)
            {
                continue;
            }

            if (!shortestPaths.ContainsKey(currentArea))
            {
                shortestPaths[currentArea] = RunAreaDijkstra(model, currentArea);
            }

            List<AreaTransition>? segment = shortestPaths[currentArea].BuildTransitionSequence(waypoint);
            if (segment == null)
            {
                return null;
            }

            foreach (AreaTransition edge in segment)
            {
                areaWalk.Add(edge);
            }

            currentArea = waypoint;
        }

        return areaWalk;
    }

    private AreaRouteCost AreaDistance(
        AreaModel model,
        Dictionary<int, AreaShortestPathTree> shortestPaths,
        int startArea,
        int goalArea)
    {
        if (!shortestPaths.ContainsKey(startArea))
        {
            shortestPaths[startArea] = RunAreaDijkstra(model, startArea);
        }

        AreaRouteCost cost;
        if (shortestPaths[startArea].TryGetCost(goalArea, out cost))
        {
            return cost;
        }

        return AreaRouteCost.Infinity;
    }

    private AreaShortestPathTree RunAreaDijkstra(AreaModel model, int startArea)
    {
        Dictionary<int, AreaRouteCost> distance = new Dictionary<int, AreaRouteCost>();
        Dictionary<int, AreaTransition> previous = new Dictionary<int, AreaTransition>();
        PriorityQueue<int, AreaRouteCost> queue = new PriorityQueue<int, AreaRouteCost>();

        distance[startArea] = new AreaRouteCost(0, 0);
        queue.Enqueue(startArea, distance[startArea]);

        while (queue.Count > 0)
        {
            queue.TryDequeue(out int currentArea, out AreaRouteCost currentCost);

            if (!distance.ContainsKey(currentArea) || !distance[currentArea].Equals(currentCost))
            {
                continue;
            }

            foreach (AreaTransition edge in model.Adjacency[currentArea])
            {
                AreaRouteCost newCost = new AreaRouteCost(
                    currentCost.Coins + edge.Toll,
                    currentCost.Crossings + 1
                );

                if (!distance.ContainsKey(edge.ToArea) || newCost.CompareTo(distance[edge.ToArea]) < 0)
                {
                    distance[edge.ToArea] = newCost;
                    previous[edge.ToArea] = edge;
                    queue.Enqueue(edge.ToArea, newCost);
                }
            }
        }

        return new AreaShortestPathTree(startArea, distance, previous);
    }

    private List<string>? BuildLocationRouteFromAreaWalk(
        AreaModel model,
        string start,
        string egress,
        List<AreaTransition> areaWalk)
    {
        List<string> fullRoute = new List<string>();
        HashSet<string> remainingTreasures = new HashSet<string>(treasureLocations);
        string currentLocation = start;

        remainingTreasures.Remove(currentLocation);

        foreach (AreaTransition transition in areaWalk)
        {
            remainingTreasures.Remove(currentLocation);

            List<string>? internalRoute = BuildInternalAreaRouteCollectingTreasures(
                model,
                currentLocation,
                transition.FromLocation,
                remainingTreasures
            );

            if (internalRoute == null)
            {
                return null;
            }

            AppendRouteAndUpdateTreasures(fullRoute, internalRoute, ref currentLocation, remainingTreasures);

            if (currentLocation != transition.FromLocation)
            {
                return null;
            }

            fullRoute.Add(transition.ExitName);
            currentLocation = transition.ToLocation;
            remainingTreasures.Remove(currentLocation);
        }

        List<string>? finalRoute = BuildInternalAreaRouteCollectingTreasures(
            model,
            currentLocation,
            egress,
            remainingTreasures
        );

        if (finalRoute == null)
        {
            return null;
        }

        AppendRouteAndUpdateTreasures(fullRoute, finalRoute, ref currentLocation, remainingTreasures);
        remainingTreasures.Remove(currentLocation);

        if (currentLocation != egress)
        {
            return null;
        }

        if (remainingTreasures.Count > 0)
        {
            return null;
        }

        return fullRoute;
    }

    private List<string>? BuildInternalAreaRouteCollectingTreasures(
        AreaModel model,
        string start,
        string target,
        HashSet<string> remainingTreasures)
    {
        if (!model.LocationToArea.ContainsKey(start) || !model.LocationToArea.ContainsKey(target))
        {
            return null;
        }

        int area = model.LocationToArea[start];
        if (model.LocationToArea[target] != area)
        {
            return null;
        }

        List<string> localTreasures = new List<string>();
        foreach (string treasure in remainingTreasures)
        {
            if (model.LocationToArea.ContainsKey(treasure) && model.LocationToArea[treasure] == area)
            {
                localTreasures.Add(treasure);
            }
        }

        if (localTreasures.Count == 0)
        {
            return FindShortestZeroTollExitSequence(model, start, target);
        }

        if (localTreasures.Count <= MaximumExactLocalTreasureCount)
        {
            List<string>? exactRoute = FindShortestInternalCollectRoute(
                model,
                start,
                target,
                localTreasures
            );

            if (exactRoute != null)
            {
                return exactRoute;
            }
        }

        return FindNearestInternalTreasureRoute(model, start, target, localTreasures);
    }

    private List<string>? FindShortestInternalCollectRoute(
        AreaModel model,
        string start,
        string target,
        List<string> localTreasures)
    {
        Dictionary<string, int> treasureIndex = new Dictionary<string, int>();
        for (int i = 0; i < localTreasures.Count; i++)
        {
            treasureIndex[localTreasures[i]] = i;
        }

        int startMask = 0;
        if (treasureIndex.ContainsKey(start))
        {
            startMask |= 1 << treasureIndex[start];
        }

        int goalMask = (1 << localTreasures.Count) - 1;
        var startState = (location: start, mask: startMask);

        Queue<(string location, int mask)> queue = new Queue<(string location, int mask)>();
        HashSet<(string location, int mask)> visited = new HashSet<(string location, int mask)>();
        Dictionary<(string location, int mask), PreviousLocationState> previous =
            new Dictionary<(string location, int mask), PreviousLocationState>();

        queue.Enqueue(startState);
        visited.Add(startState);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (current.location == target && current.mask == goalMask)
            {
                return BuildLocationStateExitSequence(startState, current, previous);
            }

            if (!graph.ContainsKey(current.location))
            {
                continue;
            }

            foreach (LearnedTrollExit exit in graph[current.location].Values)
            {
                if (exit.Destination == null || exit.Toll != 0)
                {
                    continue;
                }

                if (!model.LocationToArea.ContainsKey(exit.Destination) ||
                    model.LocationToArea[exit.Destination] != model.LocationToArea[start])
                {
                    continue;
                }

                int nextMask = current.mask;
                if (treasureIndex.ContainsKey(exit.Destination))
                {
                    nextMask |= 1 << treasureIndex[exit.Destination];
                }

                var nextState = (location: exit.Destination, mask: nextMask);
                if (visited.Contains(nextState))
                {
                    continue;
                }

                visited.Add(nextState);
                previous[nextState] = new PreviousLocationState(
                    current.location,
                    current.mask,
                    exit.Name
                );
                queue.Enqueue(nextState);
            }
        }

        return null;
    }

    private List<string> BuildLocationStateExitSequence(
        (string location, int mask) startState,
        (string location, int mask) goalState,
        Dictionary<(string location, int mask), PreviousLocationState> previous)
    {
        List<string> reversed = new List<string>();
        var current = goalState;

        while (!current.Equals(startState))
        {
            PreviousLocationState step = previous[current];
            reversed.Add(step.ExitUsed);
            current = (location: step.PreviousLocation, mask: step.PreviousMask);
        }

        reversed.Reverse();
        return reversed;
    }

    private List<string>? FindNearestInternalTreasureRoute(
        AreaModel model,
        string start,
        string target,
        List<string> localTreasures)
    {
        List<string> fullRoute = new List<string>();
        HashSet<string> remaining = new HashSet<string>(localTreasures);
        string currentLocation = start;

        remaining.Remove(currentLocation);

        while (remaining.Count > 0)
        {
            string? nearestTreasure = null;
            List<string>? bestSegment = null;

            foreach (string treasure in remaining)
            {
                List<string>? segment = FindShortestZeroTollExitSequence(model, currentLocation, treasure);
                if (segment == null)
                {
                    continue;
                }

                if (bestSegment == null || segment.Count < bestSegment.Count)
                {
                    bestSegment = segment;
                    nearestTreasure = treasure;
                }
            }

            if (nearestTreasure == null || bestSegment == null)
            {
                return null;
            }

            foreach (string exit in bestSegment)
            {
                fullRoute.Add(exit);
                string? destination = FindKnownDestination(currentLocation, exit);
                if (destination == null)
                {
                    return null;
                }

                currentLocation = destination;
                remaining.Remove(currentLocation);
            }
        }

        List<string>? finalSegment = FindShortestZeroTollExitSequence(model, currentLocation, target);
        if (finalSegment == null)
        {
            return null;
        }

        foreach (string exit in finalSegment)
        {
            fullRoute.Add(exit);
        }

        return fullRoute;
    }

    private List<string>? FindShortestZeroTollExitSequence(AreaModel model, string start, string goal)
    {
        if (start == goal)
        {
            return new List<string>();
        }

        if (!model.LocationToArea.ContainsKey(start) || !model.LocationToArea.ContainsKey(goal))
        {
            return null;
        }

        int area = model.LocationToArea[start];
        if (model.LocationToArea[goal] != area)
        {
            return null;
        }

        Queue<string> queue = new Queue<string>();
        HashSet<string> visited = new HashSet<string>();
        Dictionary<string, PreviousLocationOnlyStep> previous = new Dictionary<string, PreviousLocationOnlyStep>();

        queue.Enqueue(start);
        visited.Add(start);

        while (queue.Count > 0)
        {
            string current = queue.Dequeue();

            if (!graph.ContainsKey(current))
            {
                continue;
            }

            foreach (LearnedTrollExit exit in graph[current].Values)
            {
                if (exit.Destination == null || exit.Toll != 0)
                {
                    continue;
                }

                if (!model.LocationToArea.ContainsKey(exit.Destination) ||
                    model.LocationToArea[exit.Destination] != area)
                {
                    continue;
                }

                if (visited.Contains(exit.Destination))
                {
                    continue;
                }

                visited.Add(exit.Destination);
                previous[exit.Destination] = new PreviousLocationOnlyStep(current, exit.Name);

                if (exit.Destination == goal)
                {
                    return BuildExitSequence(start, goal, previous);
                }

                queue.Enqueue(exit.Destination);
            }
        }

        return null;
    }

    private void AppendRouteAndUpdateTreasures(
        List<string> fullRoute,
        List<string> segment,
        ref string currentLocation,
        HashSet<string> remainingTreasures)
    {
        remainingTreasures.Remove(currentLocation);

        foreach (string exit in segment)
        {
            fullRoute.Add(exit);
            string? destination = FindKnownDestination(currentLocation, exit);
            if (destination == null)
            {
                return;
            }

            currentLocation = destination;
            remainingTreasures.Remove(currentLocation);
        }
    }

    private List<string>? FindShortestKnownExitSequence(string start, string goal)
    {
        if (start == goal)
        {
            return new List<string>();
        }

        Queue<string> queue = new Queue<string>();
        HashSet<string> visited = new HashSet<string>();
        Dictionary<string, PreviousLocationOnlyStep> previousSteps =
            new Dictionary<string, PreviousLocationOnlyStep>();

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
                string? destination = exitEntry.Value.Destination;

                if (destination == null || visited.Contains(destination))
                {
                    continue;
                }

                visited.Add(destination);
                previousSteps[destination] = new PreviousLocationOnlyStep(currentLocation, exit);

                if (destination == goal)
                {
                    return BuildExitSequence(start, goal, previousSteps);
                }

                queue.Enqueue(destination);
            }
        }

        return null;
    }

    private List<string>? FindLowestTollLocationRoute(string start, string egress)
    {
        if (start == egress)
        {
            return new List<string>();
        }

        var distance = new Dictionary<string, LocationRouteCost>();
        var previous = new Dictionary<string, PreviousLocationOnlyStep>();
        var queue = new PriorityQueue<string, LocationRouteCost>();

        distance[start] = new LocationRouteCost(0, 0);
        queue.Enqueue(start, distance[start]);

        while (queue.Count > 0)
        {
            queue.TryDequeue(out string? current, out LocationRouteCost currentCost);

            if (current == null)
            {
                continue;
            }

            if (!distance.ContainsKey(current) || !distance[current].Equals(currentCost))
            {
                continue;
            }

            if (current == egress)
            {
                break;
            }

            if (!graph.ContainsKey(current))
            {
                continue;
            }

            foreach (LearnedTrollExit exit in graph[current].Values)
            {
                if (exit.Destination == null)
                {
                    continue;
                }

                LocationRouteCost newCost = new LocationRouteCost(
                    currentCost.Coins + exit.Toll,
                    currentCost.Moves + 1
                );

                if (!distance.ContainsKey(exit.Destination) || newCost.CompareTo(distance[exit.Destination]) < 0)
                {
                    distance[exit.Destination] = newCost;
                    previous[exit.Destination] = new PreviousLocationOnlyStep(current, exit.Name);
                    queue.Enqueue(exit.Destination, newCost);
                }
            }
        }

        if (!previous.ContainsKey(egress))
        {
            return null;
        }

        return BuildExitSequence(start, egress, previous);
    }

    private static List<string> BuildExitSequence(
        string start,
        string goal,
        Dictionary<string, PreviousLocationOnlyStep> previousSteps)
    {
        List<string> reversedExitSequence = new List<string>();
        string currentLocation = goal;

        while (currentLocation != start)
        {
            PreviousLocationOnlyStep step = previousSteps[currentLocation];
            reversedExitSequence.Add(step.ExitUsed);
            currentLocation = step.PreviousLocation;
        }

        reversedExitSequence.Reverse();
        return reversedExitSequence;
    }

    private string? FindKnownDestination(string location, string exit)
    {
        if (!graph.ContainsKey(location) || !graph[location].ContainsKey(exit))
        {
            return null;
        }

        return graph[location][exit].Destination;
    }

    private (string, string) ReturnActionAndRememberExit(
        (string command, string exit) action,
        RTBLocation currentLocation)
    {
        if (action.command == "go")
        {
            previousLocation = currentLocation.Name;
            previousExit = action.exit;
            previousToll = FindTollForExit(currentLocation, action.exit);
            previousActionWasGo = true;
        }
        else
        {
            previousActionWasGo = false;
        }

        return action;
    }

    private int FindTollForExit(RTBLocation location, string exitName)
    {
        foreach (RTBExit exit in location.Exits)
        {
            if (exit.Name == exitName)
            {
                return exit.Toll;
            }
        }

        throw new InvalidOperationException(
            $"Tried to remember toll for non-existent exit {exitName} from {location.Name}."
        );
    }

    private void EnsureLocationExists(string location)
    {
        if (!graph.ContainsKey(location))
        {
            graph[location] = new Dictionary<string, LearnedTrollExit>();
        }
    }

    private class AreaModel
    {
        public List<List<string>> Areas;
        public Dictionary<string, int> LocationToArea;
        public List<List<AreaTransition>> Adjacency;

        public AreaModel()
        {
            Areas = new List<List<string>>();
            LocationToArea = new Dictionary<string, int>();
            Adjacency = new List<List<AreaTransition>>();
        }

        public int AreaCount
        {
            get { return Areas.Count; }
        }
    }

    private class AreaTransition
    {
        public int FromArea;
        public int ToArea;
        public string FromLocation;
        public string ExitName;
        public string ToLocation;
        public int Toll;

        public AreaTransition(
            int fromArea,
            int toArea,
            string fromLocation,
            string exitName,
            string toLocation,
            int toll)
        {
            FromArea = fromArea;
            ToArea = toArea;
            FromLocation = fromLocation;
            ExitName = exitName;
            ToLocation = toLocation;
            Toll = toll;
        }
    }

    private class LearnedTrollExit
    {
        public string Name;
        public int Toll;
        public string? Destination;

        public LearnedTrollExit(string name, int toll, string? destination)
        {
            Name = name;
            Toll = toll;
            Destination = destination;
        }
    }

    private class AreaShortestPathTree
    {
        private int startArea;
        private Dictionary<int, AreaRouteCost> distance;
        private Dictionary<int, AreaTransition> previous;

        public AreaShortestPathTree(
            int startArea,
            Dictionary<int, AreaRouteCost> distance,
            Dictionary<int, AreaTransition> previous)
        {
            this.startArea = startArea;
            this.distance = distance;
            this.previous = previous;
        }

        public bool TryGetCost(int goalArea, out AreaRouteCost cost)
        {
            return distance.TryGetValue(goalArea, out cost);
        }

        public List<AreaTransition>? BuildTransitionSequence(int goalArea)
        {
            if (!distance.ContainsKey(goalArea))
            {
                return null;
            }

            List<AreaTransition> reversed = new List<AreaTransition>();
            int currentArea = goalArea;

            while (currentArea != startArea)
            {
                AreaTransition edge = previous[currentArea];
                reversed.Add(edge);
                currentArea = edge.FromArea;
            }

            reversed.Reverse();
            return reversed;
        }
    }

    private readonly struct PreviousAreaState
    {
        public readonly int PreviousArea;
        public readonly int PreviousMask;
        public readonly AreaTransition Transition;

        public PreviousAreaState(int previousArea, int previousMask, AreaTransition transition)
        {
            PreviousArea = previousArea;
            PreviousMask = previousMask;
            Transition = transition;
        }
    }

    private readonly struct PreviousLocationState
    {
        public readonly string PreviousLocation;
        public readonly int PreviousMask;
        public readonly string ExitUsed;

        public PreviousLocationState(string previousLocation, int previousMask, string exitUsed)
        {
            PreviousLocation = previousLocation;
            PreviousMask = previousMask;
            ExitUsed = exitUsed;
        }
    }

    private readonly struct PreviousLocationOnlyStep
    {
        public readonly string PreviousLocation;
        public readonly string ExitUsed;

        public PreviousLocationOnlyStep(string previousLocation, string exitUsed)
        {
            PreviousLocation = previousLocation;
            ExitUsed = exitUsed;
        }
    }

    private readonly struct AreaRouteCost : IComparable<AreaRouteCost>, IEquatable<AreaRouteCost>
    {
        public static readonly AreaRouteCost Infinity = new AreaRouteCost(int.MaxValue, int.MaxValue);

        public readonly int Coins;
        public readonly int Crossings;

        public AreaRouteCost(int coins, int crossings)
        {
            Coins = coins;
            Crossings = crossings;
        }

        public bool IsInfinity
        {
            get { return Coins == int.MaxValue; }
        }

        public int CompareTo(AreaRouteCost other)
        {
            int coinCompare = Coins.CompareTo(other.Coins);
            if (coinCompare != 0)
            {
                return coinCompare;
            }

            return Crossings.CompareTo(other.Crossings);
        }

        public bool Equals(AreaRouteCost other)
        {
            return Coins == other.Coins && Crossings == other.Crossings;
        }

        public override bool Equals(object? obj)
        {
            return obj is AreaRouteCost other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Coins, Crossings);
        }
    }

    private readonly struct LocationRouteCost : IComparable<LocationRouteCost>, IEquatable<LocationRouteCost>
    {
        public readonly int Coins;
        public readonly int Moves;

        public LocationRouteCost(int coins, int moves)
        {
            Coins = coins;
            Moves = moves;
        }

        public int CompareTo(LocationRouteCost other)
        {
            int coinCompare = Coins.CompareTo(other.Coins);
            if (coinCompare != 0)
            {
                return coinCompare;
            }

            return Moves.CompareTo(other.Moves);
        }

        public bool Equals(LocationRouteCost other)
        {
            return Coins == other.Coins && Moves == other.Moves;
        }

        public override bool Equals(object? obj)
        {
            return obj is LocationRouteCost other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Coins, Moves);
        }
    }
}

/// <summary>
/// Solves Ropes 100% by learning the full map, compressing toll-free/rope-free
/// locations into areas, choosing a minimum-rope set of river bridges connecting
/// the start area, egress area, and all treasure areas, then walking that tree.
/// Rope is only paid the first time a river bridge is built, so this planner
/// optimises the set of bridges rather than the number of crossings.
/// </summary>
public class Ropes100PercentChallenge
{
    private const int MaximumExactSteinerTerminalCount = 15;
    private const int MaximumExactLocalTreasureCount = 14;
    private const int Infinity = 1000000000;

    private bool learning = true;

    private Dictionary<string, Dictionary<string, LearnedRopeExit>> graph;
    private HashSet<string> treasureLocations;
    private Queue<(string command, string exit)> learningActions;
    private Queue<(string command, string exit)> challengeActions;

    private string? learningStart;
    private string? previousLocation;
    private string? previousExit;
    private int previousRopeCost;
    private bool previousActionWasGo;

    public Ropes100PercentChallenge()
    {
        graph = new Dictionary<string, Dictionary<string, LearnedRopeExit>>();
        treasureLocations = new HashSet<string>();
        learningActions = new Queue<(string command, string exit)>();
        challengeActions = new Queue<(string command, string exit)>();

        learningStart = null;
        previousLocation = null;
        previousExit = null;
        previousRopeCost = 0;
        previousActionWasGo = false;
    }

    public (string, string) ChooseNextAction(RTBLocation location)
    {
        RecordLocationAndExits(location);

        if (learning)
        {
            if (learningActions.Count > 0)
            {
                return ReturnActionAndRememberExit(learningActions.Dequeue(), location);
            }

            return ChooseLearningAction(location);
        }

        if (challengeActions.Count > 0)
        {
            return ReturnActionAndRememberExit(challengeActions.Dequeue(), location);
        }

        return ("challenge", "");
    }

    public void PrepareChallengeRoute(string start, string egress)
    {
        learning = false;
        challengeActions.Clear();
        previousActionWasGo = false;

        List<string>? route = BuildRopes100Route(start, egress);

        if (route == null)
        {
            route = FindShortestKnownExitSequence(start, egress);
        }

        if (route == null)
        {
            return;
        }

        foreach (string exit in route)
        {
            challengeActions.Enqueue(("go", exit));
        }
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
            EnsureLocationExists(previousLocation);

            if (!graph[previousLocation].ContainsKey(previousExit))
            {
                graph[previousLocation][previousExit] =
                    new LearnedRopeExit(previousExit, previousRopeCost, location.Name);
            }
            else
            {
                graph[previousLocation][previousExit].RopeCost = previousRopeCost;
                graph[previousLocation][previousExit].Destination = location.Name;
            }
        }

        previousActionWasGo = false;
    }

    private void AddKnownExitsFromLocation(RTBLocation location)
    {
        EnsureLocationExists(location.Name);

        foreach (RTBExit exit in location.Exits)
        {
            if (!graph[location.Name].ContainsKey(exit.Name))
            {
                graph[location.Name][exit.Name] = new LearnedRopeExit(exit.Name, exit.RopeCost, null);
            }
            else
            {
                graph[location.Name][exit.Name].RopeCost = exit.RopeCost;
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

        List<string>? routeToTarget = FindShortestKnownExitSequence(location.Name, targetLocation);

        if (routeToTarget == null)
        {
            return ("reset", "");
        }

        foreach (string exit in routeToTarget)
        {
            learningActions.Enqueue(("go", exit));
        }

        learningActions.Enqueue(("go", exitToExplore));
        learningActions.Enqueue(("reset", ""));

        return ReturnActionAndRememberExit(learningActions.Dequeue(), location);
    }

    private (string location, string exit)? FindExitWithUnknownDestination()
    {
        foreach (var locationEntry in graph)
        {
            string location = locationEntry.Key;
            Dictionary<string, LearnedRopeExit> exits = locationEntry.Value;

            foreach (var exitEntry in exits)
            {
                string exit = exitEntry.Key;
                LearnedRopeExit exitInfo = exitEntry.Value;

                if (exitInfo.Destination == null)
                {
                    return (location, exit);
                }
            }
        }

        return null;
    }

    private List<string>? BuildRopes100Route(string start, string egress)
    {
        AreaModel model = BuildAreaModel();

        if (!model.LocationToArea.ContainsKey(start) || !model.LocationToArea.ContainsKey(egress))
        {
            return null;
        }

        int startArea = model.LocationToArea[start];
        int egressArea = model.LocationToArea[egress];
        HashSet<int> requiredAreas = FindRequiredAreas(model, startArea, egressArea);

        HashSet<int>? selectedEdgeIds;
        if (requiredAreas.Count <= 1)
        {
            selectedEdgeIds = new HashSet<int>();
        }
        else if (requiredAreas.Count == model.AreaCount)
        {
            selectedEdgeIds = FindMinimumSpanningTreeEdges(model, startArea);
        }
        else if (requiredAreas.Count <= MaximumExactSteinerTerminalCount)
        {
            selectedEdgeIds = FindExactSteinerTreeEdges(model, requiredAreas);
        }
        else
        {
            selectedEdgeIds = FindGreedySteinerTreeEdges(model, startArea, requiredAreas);
        }

        if (selectedEdgeIds == null)
        {
            return null;
        }

        List<AreaTransition>? areaWalk = BuildAreaWalkFromSelectedEdges(
            model,
            startArea,
            egressArea,
            requiredAreas,
            selectedEdgeIds
        );

        if (areaWalk == null)
        {
            return null;
        }

        return BuildLocationRouteFromAreaWalk(model, start, egress, areaWalk);
    }

    private AreaModel BuildAreaModel()
    {
        AreaModel model = new AreaModel();

        foreach (string location in graph.Keys)
        {
            if (model.LocationToArea.ContainsKey(location))
            {
                continue;
            }

            int areaId = model.Areas.Count;
            List<string> areaLocations = new List<string>();
            Queue<string> queue = new Queue<string>();

            model.Areas.Add(areaLocations);
            model.LocationToArea[location] = areaId;
            queue.Enqueue(location);

            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                areaLocations.Add(current);

                if (!graph.ContainsKey(current))
                {
                    continue;
                }

                foreach (LearnedRopeExit exit in graph[current].Values)
                {
                    if (exit.Destination == null || exit.RopeCost != 0)
                    {
                        continue;
                    }

                    if (model.LocationToArea.ContainsKey(exit.Destination))
                    {
                        continue;
                    }

                    model.LocationToArea[exit.Destination] = areaId;
                    queue.Enqueue(exit.Destination);
                }
            }
        }

        for (int i = 0; i < model.Areas.Count; i++)
        {
            model.Adjacency.Add(new List<AreaEdge>());
        }

        Dictionary<string, AreaEdge> bestEdgesByAreaPair = new Dictionary<string, AreaEdge>();

        foreach (var locationEntry in graph)
        {
            string fromLocation = locationEntry.Key;
            if (!model.LocationToArea.ContainsKey(fromLocation))
            {
                continue;
            }

            int fromArea = model.LocationToArea[fromLocation];

            foreach (LearnedRopeExit exit in locationEntry.Value.Values)
            {
                if (exit.Destination == null || exit.RopeCost == 0)
                {
                    continue;
                }

                if (!model.LocationToArea.ContainsKey(exit.Destination))
                {
                    continue;
                }

                int toArea = model.LocationToArea[exit.Destination];
                if (fromArea == toArea)
                {
                    continue;
                }

                LearnedRopeExit? reverseExit = FindReverseExit(exit.Destination, fromLocation, exit.RopeCost);
                if (reverseExit == null)
                {
                    continue;
                }

                AreaTransition forward = new AreaTransition(
                    fromArea,
                    toArea,
                    fromLocation,
                    exit.Name,
                    exit.Destination,
                    exit.RopeCost
                );

                AreaTransition backward = new AreaTransition(
                    toArea,
                    fromArea,
                    exit.Destination,
                    reverseExit.Name,
                    fromLocation,
                    exit.RopeCost
                );

                int areaA = Math.Min(fromArea, toArea);
                int areaB = Math.Max(fromArea, toArea);
                string key = AreaPairKey(areaA, areaB);

                AreaTransition aToB = fromArea == areaA ? forward : backward;
                AreaTransition bToA = fromArea == areaA ? backward : forward;

                if (!bestEdgesByAreaPair.ContainsKey(key) ||
                    exit.RopeCost < bestEdgesByAreaPair[key].RopeCost)
                {
                    bestEdgesByAreaPair[key] = new AreaEdge(
                        -1,
                        areaA,
                        areaB,
                        exit.RopeCost,
                        aToB,
                        bToA
                    );
                }
            }
        }

        foreach (AreaEdge edge in bestEdgesByAreaPair.Values)
        {
            edge.Id = model.Edges.Count;
            model.Edges.Add(edge);
            model.Adjacency[edge.AreaA].Add(edge);
            model.Adjacency[edge.AreaB].Add(edge);
        }

        return model;
    }

    private string AreaPairKey(int areaA, int areaB)
    {
        return areaA + ":" + areaB;
    }

    private LearnedRopeExit? FindReverseExit(string fromLocation, string toLocation, int ropeCost)
    {
        if (!graph.ContainsKey(fromLocation))
        {
            return null;
        }

        foreach (LearnedRopeExit exit in graph[fromLocation].Values)
        {
            if (exit.Destination == toLocation && exit.RopeCost == ropeCost)
            {
                return exit;
            }
        }

        return null;
    }

    private HashSet<int> FindRequiredAreas(AreaModel model, int startArea, int egressArea)
    {
        HashSet<int> requiredAreas = new HashSet<int>();
        requiredAreas.Add(startArea);
        requiredAreas.Add(egressArea);

        foreach (string treasure in treasureLocations)
        {
            if (model.LocationToArea.ContainsKey(treasure))
            {
                requiredAreas.Add(model.LocationToArea[treasure]);
            }
        }

        return requiredAreas;
    }

    private HashSet<int>? FindMinimumSpanningTreeEdges(AreaModel model, int startArea)
    {
        HashSet<int> selectedEdges = new HashSet<int>();
        HashSet<int> visited = new HashSet<int>();
        PriorityQueue<AreaEdge, AreaEdgeCost> queue = new PriorityQueue<AreaEdge, AreaEdgeCost>();

        visited.Add(startArea);
        foreach (AreaEdge edge in model.Adjacency[startArea])
        {
            queue.Enqueue(edge, new AreaEdgeCost(edge.RopeCost, edge.Id));
        }

        while (queue.Count > 0 && visited.Count < model.AreaCount)
        {
            queue.TryDequeue(out AreaEdge? edge, out AreaEdgeCost cost);
            if (edge == null)
            {
                continue;
            }

            int nextArea;
            if (visited.Contains(edge.AreaA) && !visited.Contains(edge.AreaB))
            {
                nextArea = edge.AreaB;
            }
            else if (visited.Contains(edge.AreaB) && !visited.Contains(edge.AreaA))
            {
                nextArea = edge.AreaA;
            }
            else
            {
                continue;
            }

            selectedEdges.Add(edge.Id);
            visited.Add(nextArea);

            foreach (AreaEdge nextEdge in model.Adjacency[nextArea])
            {
                int other = nextEdge.Other(nextArea);
                if (!visited.Contains(other))
                {
                    queue.Enqueue(nextEdge, new AreaEdgeCost(nextEdge.RopeCost, nextEdge.Id));
                }
            }
        }

        if (visited.Count != model.AreaCount)
        {
            return null;
        }

        return selectedEdges;
    }

    private HashSet<int>? FindExactSteinerTreeEdges(AreaModel model, HashSet<int> requiredAreas)
    {
        List<int> terminals = new List<int>(requiredAreas);
        int terminalCount = terminals.Count;
        int areaCount = model.AreaCount;
        int maskCount = 1 << terminalCount;
        int fullMask = maskCount - 1;

        int[,] cost = new int[maskCount, areaCount];
        int[,] parentType = new int[maskCount, areaCount];
        int[,] parentA = new int[maskCount, areaCount];
        int[,] parentB = new int[maskCount, areaCount];

        for (int mask = 0; mask < maskCount; mask++)
        {
            for (int area = 0; area < areaCount; area++)
            {
                cost[mask, area] = Infinity;
                parentType[mask, area] = 0;
                parentA[mask, area] = -1;
                parentB[mask, area] = -1;
            }
        }

        for (int i = 0; i < terminalCount; i++)
        {
            int mask = 1 << i;
            int terminalArea = terminals[i];
            cost[mask, terminalArea] = 0;
            parentType[mask, terminalArea] = 1;
        }

        for (int mask = 1; mask < maskCount; mask++)
        {
            for (int submask = (mask - 1) & mask; submask > 0; submask = (submask - 1) & mask)
            {
                int otherMask = mask ^ submask;
                if (otherMask == 0 || submask > otherMask)
                {
                    continue;
                }

                for (int area = 0; area < areaCount; area++)
                {
                    if (cost[submask, area] == Infinity || cost[otherMask, area] == Infinity)
                    {
                        continue;
                    }

                    long mergedCost = (long)cost[submask, area] + cost[otherMask, area];
                    if (mergedCost < cost[mask, area])
                    {
                        cost[mask, area] = (int)mergedCost;
                        parentType[mask, area] = 2;
                        parentA[mask, area] = submask;
                        parentB[mask, area] = otherMask;
                    }
                }
            }

            RunSteinerDijkstraForMask(model, mask, cost, parentType, parentA, parentB);
        }

        int bestRoot = -1;
        int bestCost = Infinity;
        for (int area = 0; area < areaCount; area++)
        {
            if (cost[fullMask, area] < bestCost)
            {
                bestCost = cost[fullMask, area];
                bestRoot = area;
            }
        }

        if (bestRoot < 0 || bestCost == Infinity)
        {
            return null;
        }

        HashSet<int> selectedEdges = new HashSet<int>();
        AddSteinerEdges(fullMask, bestRoot, selectedEdges, parentType, parentA, parentB);
        return selectedEdges;
    }

    private void RunSteinerDijkstraForMask(
        AreaModel model,
        int mask,
        int[,] cost,
        int[,] parentType,
        int[,] parentA,
        int[,] parentB)
    {
        PriorityQueue<int, AreaEdgeCost> queue = new PriorityQueue<int, AreaEdgeCost>();

        for (int area = 0; area < model.AreaCount; area++)
        {
            if (cost[mask, area] < Infinity)
            {
                queue.Enqueue(area, new AreaEdgeCost(cost[mask, area], area));
            }
        }

        while (queue.Count > 0)
        {
            queue.TryDequeue(out int currentArea, out AreaEdgeCost currentCost);

            if (cost[mask, currentArea] != currentCost.RopeCost)
            {
                continue;
            }

            foreach (AreaEdge edge in model.Adjacency[currentArea])
            {
                int nextArea = edge.Other(currentArea);
                long newCost = (long)currentCost.RopeCost + edge.RopeCost;

                if (newCost < cost[mask, nextArea])
                {
                    cost[mask, nextArea] = (int)newCost;
                    parentType[mask, nextArea] = 3;
                    parentA[mask, nextArea] = currentArea;
                    parentB[mask, nextArea] = edge.Id;
                    queue.Enqueue(nextArea, new AreaEdgeCost((int)newCost, nextArea));
                }
            }
        }
    }

    private void AddSteinerEdges(
        int mask,
        int area,
        HashSet<int> selectedEdges,
        int[,] parentType,
        int[,] parentA,
        int[,] parentB)
    {
        int type = parentType[mask, area];

        if (type == 1)
        {
            return;
        }

        if (type == 2)
        {
            AddSteinerEdges(parentA[mask, area], area, selectedEdges, parentType, parentA, parentB);
            AddSteinerEdges(parentB[mask, area], area, selectedEdges, parentType, parentA, parentB);
            return;
        }

        if (type == 3)
        {
            selectedEdges.Add(parentB[mask, area]);
            AddSteinerEdges(mask, parentA[mask, area], selectedEdges, parentType, parentA, parentB);
        }
    }

    private HashSet<int>? FindGreedySteinerTreeEdges(
        AreaModel model,
        int startArea,
        HashSet<int> requiredAreas)
    {
        HashSet<int> selectedEdges = new HashSet<int>();
        HashSet<int> connectedAreas = new HashSet<int>();
        connectedAreas.Add(startArea);

        while (!AllRequiredAreasConnected(requiredAreas, connectedAreas))
        {
            List<int>? pathEdges = FindCheapestPathFromConnectedToRequired(
                model,
                connectedAreas,
                requiredAreas
            );

            if (pathEdges == null)
            {
                return null;
            }

            foreach (int edgeId in pathEdges)
            {
                selectedEdges.Add(edgeId);
                AreaEdge edge = model.Edges[edgeId];
                connectedAreas.Add(edge.AreaA);
                connectedAreas.Add(edge.AreaB);
            }
        }

        return selectedEdges;
    }

    private bool AllRequiredAreasConnected(HashSet<int> requiredAreas, HashSet<int> connectedAreas)
    {
        foreach (int area in requiredAreas)
        {
            if (!connectedAreas.Contains(area))
            {
                return false;
            }
        }

        return true;
    }

    private List<int>? FindCheapestPathFromConnectedToRequired(
        AreaModel model,
        HashSet<int> connectedAreas,
        HashSet<int> requiredAreas)
    {
        int[] distance = new int[model.AreaCount];
        int[] previousArea = new int[model.AreaCount];
        int[] previousEdge = new int[model.AreaCount];
        PriorityQueue<int, AreaEdgeCost> queue = new PriorityQueue<int, AreaEdgeCost>();

        for (int area = 0; area < model.AreaCount; area++)
        {
            distance[area] = Infinity;
            previousArea[area] = -1;
            previousEdge[area] = -1;
        }

        foreach (int area in connectedAreas)
        {
            distance[area] = 0;
            queue.Enqueue(area, new AreaEdgeCost(0, area));
        }

        int targetArea = -1;
        while (queue.Count > 0)
        {
            queue.TryDequeue(out int currentArea, out AreaEdgeCost currentCost);

            if (distance[currentArea] != currentCost.RopeCost)
            {
                continue;
            }

            if (requiredAreas.Contains(currentArea) && !connectedAreas.Contains(currentArea))
            {
                targetArea = currentArea;
                break;
            }

            foreach (AreaEdge edge in model.Adjacency[currentArea])
            {
                int nextArea = edge.Other(currentArea);
                int newCost = currentCost.RopeCost + edge.RopeCost;

                if (newCost < distance[nextArea])
                {
                    distance[nextArea] = newCost;
                    previousArea[nextArea] = currentArea;
                    previousEdge[nextArea] = edge.Id;
                    queue.Enqueue(nextArea, new AreaEdgeCost(newCost, nextArea));
                }
            }
        }

        if (targetArea < 0)
        {
            return null;
        }

        List<int> pathEdges = new List<int>();
        int current = targetArea;
        while (!connectedAreas.Contains(current))
        {
            if (previousArea[current] < 0 || previousEdge[current] < 0)
            {
                return null;
            }

            pathEdges.Add(previousEdge[current]);
            current = previousArea[current];
        }

        return pathEdges;
    }

    private List<AreaTransition>? BuildAreaWalkFromSelectedEdges(
        AreaModel model,
        int startArea,
        int egressArea,
        HashSet<int> requiredAreas,
        HashSet<int> selectedEdgeIds)
    {
        List<List<AreaEdge>> treeAdjacency = BuildSpanningTreeFromSelectedEdges(model, startArea, selectedEdgeIds);

        foreach (int requiredArea in requiredAreas)
        {
            if (!AreaIsReachableInTree(treeAdjacency, startArea, requiredArea))
            {
                return null;
            }
        }

        Dictionary<int, int>? nextOnPath = FindNextAreaOnPath(treeAdjacency, startArea, egressArea);
        if (nextOnPath == null)
        {
            return null;
        }

        List<AreaTransition> walk = new List<AreaTransition>();

        if (startArea == egressArea)
        {
            AppendReturnWalk(startArea, -1, treeAdjacency, walk);
        }
        else
        {
            AppendEgressWalk(startArea, -1, nextOnPath, treeAdjacency, walk);
        }

        return walk;
    }

    private List<List<AreaEdge>> BuildSpanningTreeFromSelectedEdges(
        AreaModel model,
        int startArea,
        HashSet<int> selectedEdgeIds)
    {
        List<List<AreaEdge>> selectedAdjacency = new List<List<AreaEdge>>();
        List<List<AreaEdge>> treeAdjacency = new List<List<AreaEdge>>();

        for (int area = 0; area < model.AreaCount; area++)
        {
            selectedAdjacency.Add(new List<AreaEdge>());
            treeAdjacency.Add(new List<AreaEdge>());
        }

        foreach (int edgeId in selectedEdgeIds)
        {
            AreaEdge edge = model.Edges[edgeId];
            selectedAdjacency[edge.AreaA].Add(edge);
            selectedAdjacency[edge.AreaB].Add(edge);
        }

        HashSet<int> visited = new HashSet<int>();
        Queue<int> queue = new Queue<int>();
        visited.Add(startArea);
        queue.Enqueue(startArea);

        while (queue.Count > 0)
        {
            int area = queue.Dequeue();

            foreach (AreaEdge edge in selectedAdjacency[area])
            {
                int other = edge.Other(area);
                if (visited.Contains(other))
                {
                    continue;
                }

                visited.Add(other);
                queue.Enqueue(other);
                treeAdjacency[area].Add(edge);
                treeAdjacency[other].Add(edge);
            }
        }

        return treeAdjacency;
    }

    private bool AreaIsReachableInTree(List<List<AreaEdge>> treeAdjacency, int startArea, int goalArea)
    {
        if (startArea == goalArea)
        {
            return true;
        }

        HashSet<int> visited = new HashSet<int>();
        Queue<int> queue = new Queue<int>();
        visited.Add(startArea);
        queue.Enqueue(startArea);

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            foreach (AreaEdge edge in treeAdjacency[current])
            {
                int next = edge.Other(current);
                if (visited.Contains(next))
                {
                    continue;
                }

                if (next == goalArea)
                {
                    return true;
                }

                visited.Add(next);
                queue.Enqueue(next);
            }
        }

        return false;
    }

    private Dictionary<int, int>? FindNextAreaOnPath(
        List<List<AreaEdge>> treeAdjacency,
        int startArea,
        int egressArea)
    {
        Dictionary<int, int> parent = new Dictionary<int, int>();
        Queue<int> queue = new Queue<int>();
        HashSet<int> visited = new HashSet<int>();

        visited.Add(startArea);
        queue.Enqueue(startArea);

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            if (current == egressArea)
            {
                break;
            }

            foreach (AreaEdge edge in treeAdjacency[current])
            {
                int next = edge.Other(current);
                if (visited.Contains(next))
                {
                    continue;
                }

                visited.Add(next);
                parent[next] = current;
                queue.Enqueue(next);
            }
        }

        if (!visited.Contains(egressArea))
        {
            return null;
        }

        Dictionary<int, int> nextOnPath = new Dictionary<int, int>();
        int areaOnPath = egressArea;

        while (areaOnPath != startArea)
        {
            int previous = parent[areaOnPath];
            nextOnPath[previous] = areaOnPath;
            areaOnPath = previous;
        }

        return nextOnPath;
    }

    private void AppendReturnWalk(
        int area,
        int parentArea,
        List<List<AreaEdge>> treeAdjacency,
        List<AreaTransition> walk)
    {
        foreach (AreaEdge edge in treeAdjacency[area])
        {
            int child = edge.Other(area);
            if (child == parentArea)
            {
                continue;
            }

            walk.Add(edge.GetTransitionFrom(area));
            AppendReturnWalk(child, area, treeAdjacency, walk);
            walk.Add(edge.GetTransitionFrom(child));
        }
    }

    private void AppendEgressWalk(
        int area,
        int parentArea,
        Dictionary<int, int> nextOnPath,
        List<List<AreaEdge>> treeAdjacency,
        List<AreaTransition> walk)
    {
        int nextPathArea = nextOnPath.ContainsKey(area) ? nextOnPath[area] : -1;

        foreach (AreaEdge edge in treeAdjacency[area])
        {
            int child = edge.Other(area);
            if (child == parentArea || child == nextPathArea)
            {
                continue;
            }

            walk.Add(edge.GetTransitionFrom(area));
            AppendReturnWalk(child, area, treeAdjacency, walk);
            walk.Add(edge.GetTransitionFrom(child));
        }

        if (nextPathArea >= 0)
        {
            AreaEdge? pathEdge = FindTreeEdge(treeAdjacency, area, nextPathArea);
            if (pathEdge != null)
            {
                walk.Add(pathEdge.GetTransitionFrom(area));
                AppendEgressWalk(nextPathArea, area, nextOnPath, treeAdjacency, walk);
            }
        }
    }

    private AreaEdge? FindTreeEdge(List<List<AreaEdge>> treeAdjacency, int areaA, int areaB)
    {
        foreach (AreaEdge edge in treeAdjacency[areaA])
        {
            if (edge.Other(areaA) == areaB)
            {
                return edge;
            }
        }

        return null;
    }

    private List<string>? BuildLocationRouteFromAreaWalk(
        AreaModel model,
        string start,
        string egress,
        List<AreaTransition> areaWalk)
    {
        List<string> fullRoute = new List<string>();
        HashSet<string> remainingTreasures = new HashSet<string>(treasureLocations);
        string currentLocation = start;

        remainingTreasures.Remove(currentLocation);

        foreach (AreaTransition transition in areaWalk)
        {
            remainingTreasures.Remove(currentLocation);

            List<string>? internalRoute = BuildInternalAreaRouteCollectingTreasures(
                model,
                currentLocation,
                transition.FromLocation,
                remainingTreasures
            );

            if (internalRoute == null)
            {
                return null;
            }

            AppendRouteAndUpdateTreasures(fullRoute, internalRoute, ref currentLocation, remainingTreasures);

            if (currentLocation != transition.FromLocation)
            {
                return null;
            }

            fullRoute.Add(transition.ExitName);
            currentLocation = transition.ToLocation;
            remainingTreasures.Remove(currentLocation);
        }

        List<string>? finalRoute = BuildInternalAreaRouteCollectingTreasures(
            model,
            currentLocation,
            egress,
            remainingTreasures
        );

        if (finalRoute == null)
        {
            return null;
        }

        AppendRouteAndUpdateTreasures(fullRoute, finalRoute, ref currentLocation, remainingTreasures);
        remainingTreasures.Remove(currentLocation);

        if (currentLocation != egress)
        {
            return null;
        }

        if (remainingTreasures.Count > 0)
        {
            return null;
        }

        return fullRoute;
    }

    private List<string>? BuildInternalAreaRouteCollectingTreasures(
        AreaModel model,
        string start,
        string target,
        HashSet<string> remainingTreasures)
    {
        if (!model.LocationToArea.ContainsKey(start) || !model.LocationToArea.ContainsKey(target))
        {
            return null;
        }

        int area = model.LocationToArea[start];
        if (model.LocationToArea[target] != area)
        {
            return null;
        }

        List<string> localTreasures = new List<string>();
        foreach (string treasure in remainingTreasures)
        {
            if (model.LocationToArea.ContainsKey(treasure) && model.LocationToArea[treasure] == area)
            {
                localTreasures.Add(treasure);
            }
        }

        if (localTreasures.Count == 0)
        {
            return FindShortestZeroRopeExitSequence(model, start, target);
        }

        if (localTreasures.Count <= MaximumExactLocalTreasureCount)
        {
            List<string>? exactRoute = FindShortestInternalCollectRoute(
                model,
                start,
                target,
                localTreasures
            );

            if (exactRoute != null)
            {
                return exactRoute;
            }
        }

        return FindNearestInternalTreasureRoute(model, start, target, localTreasures);
    }

    private List<string>? FindShortestInternalCollectRoute(
        AreaModel model,
        string start,
        string target,
        List<string> localTreasures)
    {
        Dictionary<string, int> treasureIndex = new Dictionary<string, int>();
        for (int i = 0; i < localTreasures.Count; i++)
        {
            treasureIndex[localTreasures[i]] = i;
        }

        int startMask = 0;
        if (treasureIndex.ContainsKey(start))
        {
            startMask |= 1 << treasureIndex[start];
        }

        int goalMask = (1 << localTreasures.Count) - 1;
        var startState = (location: start, mask: startMask);

        Queue<(string location, int mask)> queue = new Queue<(string location, int mask)>();
        HashSet<(string location, int mask)> visited = new HashSet<(string location, int mask)>();
        Dictionary<(string location, int mask), PreviousLocationState> previous =
            new Dictionary<(string location, int mask), PreviousLocationState>();

        queue.Enqueue(startState);
        visited.Add(startState);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (current.location == target && current.mask == goalMask)
            {
                return BuildLocationStateExitSequence(startState, current, previous);
            }

            if (!graph.ContainsKey(current.location))
            {
                continue;
            }

            foreach (LearnedRopeExit exit in graph[current.location].Values)
            {
                if (exit.Destination == null || exit.RopeCost != 0)
                {
                    continue;
                }

                if (!model.LocationToArea.ContainsKey(exit.Destination) ||
                    model.LocationToArea[exit.Destination] != model.LocationToArea[start])
                {
                    continue;
                }

                int nextMask = current.mask;
                if (treasureIndex.ContainsKey(exit.Destination))
                {
                    nextMask |= 1 << treasureIndex[exit.Destination];
                }

                var nextState = (location: exit.Destination, mask: nextMask);
                if (visited.Contains(nextState))
                {
                    continue;
                }

                visited.Add(nextState);
                previous[nextState] = new PreviousLocationState(
                    current.location,
                    current.mask,
                    exit.Name
                );
                queue.Enqueue(nextState);
            }
        }

        return null;
    }

    private List<string> BuildLocationStateExitSequence(
        (string location, int mask) startState,
        (string location, int mask) goalState,
        Dictionary<(string location, int mask), PreviousLocationState> previous)
    {
        List<string> reversed = new List<string>();
        var current = goalState;

        while (!current.Equals(startState))
        {
            PreviousLocationState step = previous[current];
            reversed.Add(step.ExitUsed);
            current = (location: step.PreviousLocation, mask: step.PreviousMask);
        }

        reversed.Reverse();
        return reversed;
    }

    private List<string>? FindNearestInternalTreasureRoute(
        AreaModel model,
        string start,
        string target,
        List<string> localTreasures)
    {
        List<string> fullRoute = new List<string>();
        HashSet<string> remaining = new HashSet<string>(localTreasures);
        string currentLocation = start;

        remaining.Remove(currentLocation);

        while (remaining.Count > 0)
        {
            string? nearestTreasure = null;
            List<string>? bestSegment = null;

            foreach (string treasure in remaining)
            {
                List<string>? segment = FindShortestZeroRopeExitSequence(model, currentLocation, treasure);
                if (segment == null)
                {
                    continue;
                }

                if (bestSegment == null || segment.Count < bestSegment.Count)
                {
                    bestSegment = segment;
                    nearestTreasure = treasure;
                }
            }

            if (nearestTreasure == null || bestSegment == null)
            {
                return null;
            }

            foreach (string exit in bestSegment)
            {
                fullRoute.Add(exit);
                string? destination = FindKnownDestination(currentLocation, exit);
                if (destination == null)
                {
                    return null;
                }

                currentLocation = destination;
                remaining.Remove(currentLocation);
            }
        }

        List<string>? finalSegment = FindShortestZeroRopeExitSequence(model, currentLocation, target);
        if (finalSegment == null)
        {
            return null;
        }

        foreach (string exit in finalSegment)
        {
            fullRoute.Add(exit);
        }

        return fullRoute;
    }

    private List<string>? FindShortestZeroRopeExitSequence(AreaModel model, string start, string goal)
    {
        if (start == goal)
        {
            return new List<string>();
        }

        if (!model.LocationToArea.ContainsKey(start) || !model.LocationToArea.ContainsKey(goal))
        {
            return null;
        }

        int area = model.LocationToArea[start];
        if (model.LocationToArea[goal] != area)
        {
            return null;
        }

        Queue<string> queue = new Queue<string>();
        HashSet<string> visited = new HashSet<string>();
        Dictionary<string, PreviousLocationOnlyStep> previous = new Dictionary<string, PreviousLocationOnlyStep>();

        queue.Enqueue(start);
        visited.Add(start);

        while (queue.Count > 0)
        {
            string current = queue.Dequeue();

            if (!graph.ContainsKey(current))
            {
                continue;
            }

            foreach (LearnedRopeExit exit in graph[current].Values)
            {
                if (exit.Destination == null || exit.RopeCost != 0)
                {
                    continue;
                }

                if (!model.LocationToArea.ContainsKey(exit.Destination) ||
                    model.LocationToArea[exit.Destination] != area)
                {
                    continue;
                }

                if (visited.Contains(exit.Destination))
                {
                    continue;
                }

                visited.Add(exit.Destination);
                previous[exit.Destination] = new PreviousLocationOnlyStep(current, exit.Name);

                if (exit.Destination == goal)
                {
                    return BuildExitSequence(start, goal, previous);
                }

                queue.Enqueue(exit.Destination);
            }
        }

        return null;
    }

    private void AppendRouteAndUpdateTreasures(
        List<string> fullRoute,
        List<string> segment,
        ref string currentLocation,
        HashSet<string> remainingTreasures)
    {
        remainingTreasures.Remove(currentLocation);

        foreach (string exit in segment)
        {
            fullRoute.Add(exit);
            string? destination = FindKnownDestination(currentLocation, exit);
            if (destination == null)
            {
                return;
            }

            currentLocation = destination;
            remainingTreasures.Remove(currentLocation);
        }
    }

    private List<string>? FindShortestKnownExitSequence(string start, string goal)
    {
        if (start == goal)
        {
            return new List<string>();
        }

        Queue<string> queue = new Queue<string>();
        HashSet<string> visited = new HashSet<string>();
        Dictionary<string, PreviousLocationOnlyStep> previousSteps =
            new Dictionary<string, PreviousLocationOnlyStep>();

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
                string? destination = exitEntry.Value.Destination;

                if (destination == null || visited.Contains(destination))
                {
                    continue;
                }

                visited.Add(destination);
                previousSteps[destination] = new PreviousLocationOnlyStep(currentLocation, exit);

                if (destination == goal)
                {
                    return BuildExitSequence(start, goal, previousSteps);
                }

                queue.Enqueue(destination);
            }
        }

        return null;
    }

    private static List<string> BuildExitSequence(
        string start,
        string goal,
        Dictionary<string, PreviousLocationOnlyStep> previousSteps)
    {
        List<string> reversedExitSequence = new List<string>();
        string currentLocation = goal;

        while (currentLocation != start)
        {
            PreviousLocationOnlyStep step = previousSteps[currentLocation];
            reversedExitSequence.Add(step.ExitUsed);
            currentLocation = step.PreviousLocation;
        }

        reversedExitSequence.Reverse();
        return reversedExitSequence;
    }

    private string? FindKnownDestination(string location, string exit)
    {
        if (!graph.ContainsKey(location) || !graph[location].ContainsKey(exit))
        {
            return null;
        }

        return graph[location][exit].Destination;
    }

    private (string, string) ReturnActionAndRememberExit(
        (string command, string exit) action,
        RTBLocation currentLocation)
    {
        if (action.command == "go")
        {
            previousLocation = currentLocation.Name;
            previousExit = action.exit;
            previousRopeCost = FindRopeCostForExit(currentLocation, action.exit);
            previousActionWasGo = true;
        }
        else
        {
            previousActionWasGo = false;
        }

        return action;
    }

    private int FindRopeCostForExit(RTBLocation location, string exitName)
    {
        foreach (RTBExit exit in location.Exits)
        {
            if (exit.Name == exitName)
            {
                return exit.RopeCost;
            }
        }

        throw new InvalidOperationException(
            $"Tried to remember rope cost for non-existent exit {exitName} from {location.Name}."
        );
    }

    private void EnsureLocationExists(string location)
    {
        if (!graph.ContainsKey(location))
        {
            graph[location] = new Dictionary<string, LearnedRopeExit>();
        }
    }

    private class AreaModel
    {
        public List<List<string>> Areas;
        public Dictionary<string, int> LocationToArea;
        public List<AreaEdge> Edges;
        public List<List<AreaEdge>> Adjacency;

        public AreaModel()
        {
            Areas = new List<List<string>>();
            LocationToArea = new Dictionary<string, int>();
            Edges = new List<AreaEdge>();
            Adjacency = new List<List<AreaEdge>>();
        }

        public int AreaCount
        {
            get { return Areas.Count; }
        }
    }

    private class AreaTransition
    {
        public int FromArea;
        public int ToArea;
        public string FromLocation;
        public string ExitName;
        public string ToLocation;
        public int RopeCost;

        public AreaTransition(
            int fromArea,
            int toArea,
            string fromLocation,
            string exitName,
            string toLocation,
            int ropeCost)
        {
            FromArea = fromArea;
            ToArea = toArea;
            FromLocation = fromLocation;
            ExitName = exitName;
            ToLocation = toLocation;
            RopeCost = ropeCost;
        }
    }

    private class AreaEdge
    {
        public int Id;
        public int AreaA;
        public int AreaB;
        public int RopeCost;
        public AreaTransition AToB;
        public AreaTransition BToA;

        public AreaEdge(
            int id,
            int areaA,
            int areaB,
            int ropeCost,
            AreaTransition aToB,
            AreaTransition bToA)
        {
            Id = id;
            AreaA = areaA;
            AreaB = areaB;
            RopeCost = ropeCost;
            AToB = aToB;
            BToA = bToA;
        }

        public int Other(int area)
        {
            if (area == AreaA)
            {
                return AreaB;
            }

            return AreaA;
        }

        public AreaTransition GetTransitionFrom(int area)
        {
            if (area == AreaA)
            {
                return AToB;
            }

            return BToA;
        }
    }

    private class LearnedRopeExit
    {
        public string Name;
        public int RopeCost;
        public string? Destination;

        public LearnedRopeExit(string name, int ropeCost, string? destination)
        {
            Name = name;
            RopeCost = ropeCost;
            Destination = destination;
        }
    }

    private readonly struct PreviousLocationState
    {
        public readonly string PreviousLocation;
        public readonly int PreviousMask;
        public readonly string ExitUsed;

        public PreviousLocationState(string previousLocation, int previousMask, string exitUsed)
        {
            PreviousLocation = previousLocation;
            PreviousMask = previousMask;
            ExitUsed = exitUsed;
        }
    }

    private readonly struct PreviousLocationOnlyStep
    {
        public readonly string PreviousLocation;
        public readonly string ExitUsed;

        public PreviousLocationOnlyStep(string previousLocation, string exitUsed)
        {
            PreviousLocation = previousLocation;
            ExitUsed = exitUsed;
        }
    }

    private readonly struct AreaEdgeCost : IComparable<AreaEdgeCost>, IEquatable<AreaEdgeCost>
    {
        public readonly int RopeCost;
        public readonly int TieBreaker;

        public AreaEdgeCost(int ropeCost, int tieBreaker)
        {
            RopeCost = ropeCost;
            TieBreaker = tieBreaker;
        }

        public int CompareTo(AreaEdgeCost other)
        {
            int ropeCompare = RopeCost.CompareTo(other.RopeCost);
            if (ropeCompare != 0)
            {
                return ropeCompare;
            }

            return TieBreaker.CompareTo(other.TieBreaker);
        }

        public bool Equals(AreaEdgeCost other)
        {
            return RopeCost == other.RopeCost && TieBreaker == other.TieBreaker;
        }

        public override bool Equals(object? obj)
        {
            return obj is AreaEdgeCost other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(RopeCost, TieBreaker);
        }
    }
}

/// <summary>
/// Solves Keys Any% by learning the full directed map, recording key colours and
/// lock colours, then planning a route from the challenge start to the egress.
/// For smaller key sets it uses an exact BFS over (location, available-key-mask),
/// which minimises go commands. For larger key sets it falls back to a scalable
/// greedy unlocking route.
/// </summary>
public class KeysAnyPercentChallenge
{
    private const long ExactStateLimit = 5000000;
    private const int MaximumExactKeyCount = 24;
    private const int UnreachableDistance = 1000000000;

    private bool learning = true;

    private Dictionary<string, Dictionary<string, LearnedKeyExit>> graph;
    private Dictionary<string, string> keyAtLocation;
    private HashSet<string> allColours;
    private Dictionary<string, int> colourToIndex;
    private Queue<(string command, string exit)> learningActions;
    private Queue<(string command, string exit)> challengeActions;

    private string challengeType;
    private string? learningStart;
    private string? previousLocation;
    private string? previousExit;
    private bool previousActionWasGo;

    public KeysAnyPercentChallenge(string challengeType)
    {
        this.challengeType = challengeType;

        graph = new Dictionary<string, Dictionary<string, LearnedKeyExit>>();
        keyAtLocation = new Dictionary<string, string>();
        allColours = new HashSet<string>();
        colourToIndex = new Dictionary<string, int>();
        learningActions = new Queue<(string command, string exit)>();
        challengeActions = new Queue<(string command, string exit)>();

        learningStart = null;
        previousLocation = null;
        previousExit = null;
        previousActionWasGo = false;
    }

    public (string, string) ChooseNextAction(RTBLocation location)
    {
        RecordLocationAndExits(location);

        if (learning)
        {
            if (learningActions.Count > 0)
            {
                return ReturnActionAndRememberExit(learningActions.Dequeue(), location.Name);
            }

            return ChooseLearningAction(location);
        }

        if (challengeActions.Count > 0)
        {
            return ReturnActionAndRememberExit(challengeActions.Dequeue(), location.Name);
        }

        // Usually the engine checks completion before asking for another action.
        return ("challenge", "");
    }

    public void PrepareChallengeRoute(string start, string egress)
    {
        learning = false;
        challengeActions.Clear();
        previousActionWasGo = false;

        BuildColourIndex();

        List<string>? route = FindExactShortestRouteWithKeys(start, egress);
        if (route == null)
        {
            route = FindGreedyUnlockingRoute(start, egress);
        }

        foreach (string exit in route)
        {
            challengeActions.Enqueue(("go", exit));
        }
    }

    private void RecordLocationAndExits(RTBLocation location)
    {
        if (learningStart == null)
        {
            learningStart = location.Name;
        }

        AddKnownExitsFromLocation(location);

        if (location.KeyColour != null)
        {
            keyAtLocation[location.Name] = location.KeyColour;
            allColours.Add(location.KeyColour);
        }

        if (previousActionWasGo && previousLocation != null && previousExit != null)
        {
            EnsureLocationExists(previousLocation);
            if (!graph[previousLocation].ContainsKey(previousExit))
            {
                graph[previousLocation][previousExit] = new LearnedKeyExit(previousExit, null, location.Name);
            }
            else
            {
                graph[previousLocation][previousExit].Destination = location.Name;
            }
        }

        previousActionWasGo = false;
    }

    private void AddKnownExitsFromLocation(RTBLocation location)
    {
        EnsureLocationExists(location.Name);

        foreach (RTBExit exit in location.Exits)
        {
            if (exit.LockColour != null)
            {
                allColours.Add(exit.LockColour);
            }

            if (!graph[location.Name].ContainsKey(exit.Name))
            {
                graph[location.Name][exit.Name] = new LearnedKeyExit(exit.Name, exit.LockColour, null);
            }
            else
            {
                graph[location.Name][exit.Name].LockColour = exit.LockColour;
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

        List<string>? routeToTarget = FindShortestKnownExitSequence(location.Name, targetLocation);

        if (routeToTarget == null)
        {
            return ("reset", "");
        }

        foreach (string exit in routeToTarget)
        {
            learningActions.Enqueue(("go", exit));
        }

        learningActions.Enqueue(("go", exitToExplore));
        learningActions.Enqueue(("reset", ""));

        return ReturnActionAndRememberExit(learningActions.Dequeue(), location.Name);
    }

    private (string location, string exit)? FindExitWithUnknownDestination()
    {
        foreach (var locationEntry in graph)
        {
            string location = locationEntry.Key;
            Dictionary<string, LearnedKeyExit> exits = locationEntry.Value;

            foreach (var exitEntry in exits)
            {
                string exit = exitEntry.Key;
                LearnedKeyExit exitInfo = exitEntry.Value;

                if (exitInfo.Destination == null)
                {
                    return (location, exit);
                }
            }
        }

        return null;
    }

    private List<string>? FindShortestKnownExitSequence(string start, string goal)
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
                string? destination = exitEntry.Value.Destination;

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

    private void BuildColourIndex()
    {
        colourToIndex.Clear();

        foreach (string colour in allColours)
        {
            if (!colourToIndex.ContainsKey(colour))
            {
                colourToIndex[colour] = colourToIndex.Count;
            }
        }
    }

    private List<string>? FindExactShortestRouteWithKeys(string start, string egress)
    {
        int colourCount = colourToIndex.Count;
        if (colourCount > MaximumExactKeyCount || colourCount >= 63)
        {
            return null;
        }

        long maskCount = 1L << colourCount;
        if (graph.Count > 0 && maskCount > ExactStateLimit / graph.Count)
        {
            return null;
        }

        ulong startMask = AddLocationKeyToMask(0UL, start);
        KeyState startState = new KeyState(start, startMask);

        if (start == egress)
        {
            return new List<string>();
        }

        Queue<KeyState> queue = new Queue<KeyState>();
        HashSet<KeyState> visited = new HashSet<KeyState>();
        Dictionary<KeyState, PreviousKeyStep> previousSteps = new Dictionary<KeyState, PreviousKeyStep>();

        queue.Enqueue(startState);
        visited.Add(startState);

        while (queue.Count > 0)
        {
            KeyState currentState = queue.Dequeue();

            if (!graph.ContainsKey(currentState.Location))
            {
                continue;
            }

            foreach (LearnedKeyExit exit in graph[currentState.Location].Values)
            {
                if (exit.Destination == null)
                {
                    continue;
                }

                if (!CanUseExit(exit, currentState.AvailableColours))
                {
                    continue;
                }

                ulong nextMask = AddLocationKeyToMask(currentState.AvailableColours, exit.Destination);
                KeyState nextState = new KeyState(exit.Destination, nextMask);

                if (visited.Contains(nextState))
                {
                    continue;
                }

                visited.Add(nextState);
                previousSteps[nextState] = new PreviousKeyStep(currentState, exit.Name);

                if (exit.Destination == egress)
                {
                    return BuildExitSequence(startState, nextState, previousSteps);
                }

                queue.Enqueue(nextState);
            }
        }

        return null;
    }

    private List<string> FindGreedyUnlockingRoute(string start, string egress)
    {
        List<string> fullRoute = new List<string>();
        HashSet<string> availableColours = new HashSet<string>();
        string currentLocation = start;

        AddLocationKeyToSet(availableColours, currentLocation);

        int safetyLimit = graph.Count + allColours.Count + 10;
        while (currentLocation != egress && safetyLimit-- > 0)
        {
            Dictionary<string, PathResult> reachable = FindReachablePathsWithAvailableKeys(currentLocation, availableColours);

            if (reachable.ContainsKey(egress))
            {
                AppendRouteAndCollectKeys(fullRoute, reachable[egress].Exits, ref currentLocation, availableColours);
                return fullRoute;
            }

            string? bestKeyLocation = null;
            int bestScore = int.MaxValue;
            int bestDistanceToKey = int.MaxValue;

            foreach (var keyEntry in keyAtLocation)
            {
                string keyLocation = keyEntry.Key;
                string colour = keyEntry.Value;

                if (availableColours.Contains(colour))
                {
                    continue;
                }

                if (!reachable.ContainsKey(keyLocation))
                {
                    continue;
                }

                int distanceToKey = reachable[keyLocation].Exits.Count;
                HashSet<string> coloursAfterKey = new HashSet<string>(availableColours);
                coloursAfterKey.Add(colour);

                int futureDistance = FindShortestDistanceWithAvailableKeys(keyLocation, egress, coloursAfterKey);
                int score = SafeAdd(distanceToKey, futureDistance);

                if (score < bestScore ||
                    (score == bestScore && distanceToKey < bestDistanceToKey))
                {
                    bestScore = score;
                    bestDistanceToKey = distanceToKey;
                    bestKeyLocation = keyLocation;
                }
            }

            if (bestKeyLocation == null)
            {
                throw new InvalidOperationException(
                    $"No Keys Any% route found from {start} to {egress}. " +
                    "The learned map may be incomplete, or the key dependency search failed."
                );
            }

            AppendRouteAndCollectKeys(fullRoute, reachable[bestKeyLocation].Exits, ref currentLocation, availableColours);
        }

        if (currentLocation == egress)
        {
            return fullRoute;
        }

        throw new InvalidOperationException(
            $"No Keys Any% route found from {start} to {egress} before the safety limit was reached."
        );
    }

    private Dictionary<string, PathResult> FindReachablePathsWithAvailableKeys(
        string start,
        HashSet<string> availableColours)
    {
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

            foreach (LearnedKeyExit exit in graph[currentLocation].Values)
            {
                if (exit.Destination == null)
                {
                    continue;
                }

                if (!CanUseExit(exit, availableColours))
                {
                    continue;
                }

                if (visited.Contains(exit.Destination))
                {
                    continue;
                }

                visited.Add(exit.Destination);
                previousSteps[exit.Destination] = (currentLocation, exit.Name);
                queue.Enqueue(exit.Destination);
            }
        }

        Dictionary<string, PathResult> paths = new Dictionary<string, PathResult>();
        foreach (string location in visited)
        {
            List<string> exits = BuildExitSequence(start, location, previousSteps);
            paths[location] = new PathResult(exits);
        }

        return paths;
    }

    private int FindShortestDistanceWithAvailableKeys(
        string start,
        string goal,
        HashSet<string> availableColours)
    {
        if (start == goal)
        {
            return 0;
        }

        Queue<string> queue = new Queue<string>();
        Dictionary<string, int> distance = new Dictionary<string, int>();

        queue.Enqueue(start);
        distance[start] = 0;

        while (queue.Count > 0)
        {
            string currentLocation = queue.Dequeue();

            if (!graph.ContainsKey(currentLocation))
            {
                continue;
            }

            foreach (LearnedKeyExit exit in graph[currentLocation].Values)
            {
                if (exit.Destination == null)
                {
                    continue;
                }

                if (!CanUseExit(exit, availableColours))
                {
                    continue;
                }

                if (distance.ContainsKey(exit.Destination))
                {
                    continue;
                }

                distance[exit.Destination] = distance[currentLocation] + 1;

                if (exit.Destination == goal)
                {
                    return distance[exit.Destination];
                }

                queue.Enqueue(exit.Destination);
            }
        }

        return UnreachableDistance;
    }

    private void AppendRouteAndCollectKeys(
        List<string> fullRoute,
        List<string> routeToAppend,
        ref string currentLocation,
        HashSet<string> availableColours)
    {
        foreach (string exitName in routeToAppend)
        {
            fullRoute.Add(exitName);

            if (!graph.ContainsKey(currentLocation) || !graph[currentLocation].ContainsKey(exitName))
            {
                throw new InvalidOperationException(
                    $"Planned route uses unknown exit {exitName} from {currentLocation}."
                );
            }

            string? destination = graph[currentLocation][exitName].Destination;
            if (destination == null)
            {
                throw new InvalidOperationException(
                    $"Planned route uses exit {exitName} from {currentLocation}, but its destination is unknown."
                );
            }

            currentLocation = destination;
            AddLocationKeyToSet(availableColours, currentLocation);
        }
    }

    private bool CanUseExit(LearnedKeyExit exit, ulong availableColours)
    {
        if (exit.LockColour == null)
        {
            return true;
        }

        if (!colourToIndex.ContainsKey(exit.LockColour))
        {
            return false;
        }

        int bit = colourToIndex[exit.LockColour];
        return (availableColours & (1UL << bit)) != 0;
    }

    private bool CanUseExit(LearnedKeyExit exit, HashSet<string> availableColours)
    {
        return exit.LockColour == null || availableColours.Contains(exit.LockColour);
    }

    private ulong AddLocationKeyToMask(ulong mask, string location)
    {
        if (!keyAtLocation.ContainsKey(location))
        {
            return mask;
        }

        string colour = keyAtLocation[location];
        if (!colourToIndex.ContainsKey(colour))
        {
            return mask;
        }

        int bit = colourToIndex[colour];
        return mask | (1UL << bit);
    }

    private void AddLocationKeyToSet(HashSet<string> colours, string location)
    {
        if (keyAtLocation.ContainsKey(location))
        {
            colours.Add(keyAtLocation[location]);
        }
    }

    private int SafeAdd(int a, int b)
    {
        if (a >= UnreachableDistance || b >= UnreachableDistance)
        {
            return UnreachableDistance;
        }

        if (a > int.MaxValue - b)
        {
            return int.MaxValue;
        }

        return a + b;
    }

    private static List<string> BuildExitSequence(
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

    private static List<string> BuildExitSequence(
        KeyState start,
        KeyState goal,
        Dictionary<KeyState, PreviousKeyStep> previousSteps)
    {
        List<string> reversedExitSequence = new List<string>();
        KeyState currentState = goal;

        while (!currentState.Equals(start))
        {
            PreviousKeyStep step = previousSteps[currentState];
            reversedExitSequence.Add(step.ExitUsed);
            currentState = step.PreviousState;
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

    private void EnsureLocationExists(string location)
    {
        if (!graph.ContainsKey(location))
        {
            graph[location] = new Dictionary<string, LearnedKeyExit>();
        }
    }

    private class LearnedKeyExit
    {
        public string Name;
        public string? LockColour;
        public string? Destination;

        public LearnedKeyExit(string name, string? lockColour, string? destination)
        {
            Name = name;
            LockColour = lockColour;
            Destination = destination;
        }
    }

    private class PathResult
    {
        public List<string> Exits;

        public PathResult(List<string> exits)
        {
            Exits = exits;
        }
    }

    private readonly struct KeyState : IEquatable<KeyState>
    {
        public readonly string Location;
        public readonly ulong AvailableColours;

        public KeyState(string location, ulong availableColours)
        {
            Location = location;
            AvailableColours = availableColours;
        }

        public bool Equals(KeyState other)
        {
            return Location == other.Location && AvailableColours == other.AvailableColours;
        }

        public override bool Equals(object? obj)
        {
            return obj is KeyState other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Location, AvailableColours);
        }
    }

    private readonly struct PreviousKeyStep
    {
        public readonly KeyState PreviousState;
        public readonly string ExitUsed;

        public PreviousKeyStep(KeyState previousState, string exitUsed)
        {
            PreviousState = previousState;
            ExitUsed = exitUsed;
        }
    }
}