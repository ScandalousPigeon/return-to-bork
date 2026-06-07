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

    public RTBPlayer(string mapType, string challengeType)
    {
        MapType = mapType;
        ChallengeType = challengeType;
        random = new Random();

        mazeSolver = new MazeSolver(challengeType);
        trollsAnyPercent = new TrollsAnyPercentChallenge();
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
}

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