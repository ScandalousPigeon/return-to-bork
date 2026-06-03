using System.Collections;
using System.Collections.Generic;

// Class for exit data shared with player
public class RTBExit
{
    public string Name { get; set; }
    public string? LockColour { get; set; }
    public int Toll { get; set; }
    public int RopeCost { get; set; }

    public RTBExit(string name, string? lockColour, int toll, int ropeCost)
    {
        Name = name;              // Use this name with "go" command
        LockColour = lockColour;  // will be null if there is no lock on this exit, otherwise needs key with matching colour to open.
        Toll = toll;              // will be 0 if this exit doesn't go over a Troll bridge.
        RopeCost = ropeCost;      // will be 0 if this exit does not cross a river.
    }
}

// Class for location data shared with player
public class RTBLocation
{
    // Properties
    public string Name { get; set; }
    public bool Treasure { get; set; }
    public List<RTBExit> Exits { get; set; }
    public string? KeyColour { get; set; }

    // Constructor
    public RTBLocation(string name, bool treasure, string keyColour, List<RTBExit> exits)
    {
        Name = name;               // name of this location.  This is unique.
        Treasure = treasure;       // true if there is treasure present at this location
        KeyColour = keyColour;     // string giving colour of key if present, null if no key
        Exits = exits;             // list of RTBExits from this location
    }
}

// Class for full location data
public class Location
{
    // Properties
    public string Name { get; set; }
    public bool Treasure { get; set; }
    public SortedDictionary<string, Exit> Exits { get; }
    public string? KeyColour { get; set; }

    // Constructor
    public Location(string name)
    {
        Name = name;
        Treasure = false;
        KeyColour = null;
        Exits = new SortedDictionary<string, Exit>();
    }

    public void AddExit(string name, Location dest, string reverseExitName)
    {
        var e = new Exit(name, dest, reverseExitName, null, 0, 0);
        Exits.Add(name, e);
    }

    public void AddSpecialExit(string name, Location dest, string reverseExitName, string? keyColour, int toll, int ropeCost)
    {
        var e = new Exit(name, dest, reverseExitName, keyColour, toll, ropeCost);
        Exits.Add(name, e);        
    }

    public RTBLocation ToRTBLocation()
    {
        var exits = new List<RTBExit>();
        foreach (var (e,E)  in Exits)
        {
            exits.Add(E.ToRTBExit());
        }
        return new RTBLocation(Name, Treasure, KeyColour, exits);
    }
}

// Class for full exit data
public class Exit
{
    // Properties
    public string Name { get; set; }
    public string? LockColour { get; set; }
    public int Toll { get; set; }
    public int RopeCost { get; set; }
    public Location Destination { get; set; }
    public string ReverseExitName { get; set; }

    // Constructor
    public Exit(string name, Location dest, string reverseExit, string? lockColour, int toll, int ropeCost)
    {
        Name = name;
        Destination = dest;
        ReverseExitName = reverseExit;
        LockColour = lockColour;
        Toll = toll;
        RopeCost = ropeCost;
    }

    public RTBExit ToRTBExit()
    {
        return new RTBExit(Name, LockColour, Toll, RopeCost);
    }
}

public class RTBMap
{
    public Dictionary<string, Location> Locations { get; set; }
    public Location? Start { get; set; }
    public Location? Egress { get; set; }
    public Location LearningModeStart { get; set; }
    public HashSet<String> TreasureLocations = new();

    private Random random;

    Location[][] AreaVertices;
    int AreaCount;
    int AreaSize;
    int MaxToll = 20;

    public RTBMap(int seed, int areaSize, int areaCount, int averageDegree)
    {
        Locations = new Dictionary<string, Location>();
        random = new Random(seed);
        AreaCount = areaCount;
        AreaSize = areaSize;
        AreaVertices = new Location[AreaCount][];
        for(int area = 0; area < AreaCount; area++) {
            AreaVertices[area] = CreateArea(AreaSize, averageDegree);
        }
        LearningModeStart = AreaVertices[random.Next(AreaCount)][random.Next(AreaSize)];
    }

    public static RTBMap MapWithTolls(int seed, int areaSize, int areaCount, int averageDegree, int treasureCount)
    {
        var map = new RTBMap(seed, areaSize, areaCount, averageDegree);

        map.Start = map.AreaVertices[map.random.Next(0, areaCount)][map.random.Next(0, areaSize)];
        map.Egress = map.AreaVertices[map.random.Next(0, areaCount)][map.random.Next(0, areaSize)];
        map.AddTreasure(treasureCount);

        // Connect up areas with toll bridges
        foreach (var (i, j) in map.RandomEdges(areaCount, averageDegree))
        {
            var u = map.AreaVertices[i][map.random.Next(0, areaSize)];
            var v = map.AreaVertices[j][map.random.Next(0, areaSize)];
            map.MakeEdge(u, v, null, map.random.Next(1, map.MaxToll + 1), 0);
        }

        return map;
    }

    public static RTBMap MapWithRopes(int seed, int areaSize, int areaCount, int averageDegree, int treasureCount)
    {
        var map = new RTBMap(seed, areaSize, areaCount, averageDegree);

        map.Start = map.AreaVertices[map.random.Next(0, areaCount)][map.random.Next(0, areaSize)];
        map.Egress = map.AreaVertices[map.random.Next(0, areaCount)][map.random.Next(0, areaSize)];
        map.AddTreasure(treasureCount);

        const int maxRopeCost = 20;
        foreach (var (i, j) in map.RandomEdges(areaCount, averageDegree))
        {
            var u = map.AreaVertices[i][map.random.Next(0, areaSize)];
            var v = map.AreaVertices[j][map.random.Next(0, areaSize)];
            map.MakeEdge(u, v, null, 0, map.random.Next(1, maxRopeCost + 1));
        }

        return map;
    }

    public static RTBMap MapWithKeys(int seed, int areaSize, int areaCount, int averageDegree)
    {
        var map = new RTBMap(seed, areaSize, areaCount, averageDegree);

        map.Start = map.AreaVertices[map.random.Next(0, areaCount)][map.random.Next(0, areaSize)];
        map.Egress = map.AreaVertices[map.random.Next(0, areaCount)][map.random.Next(0, areaSize)];

        // Connect areas with locked doors (one unique colour per inter-area edge)
        var interAreaEdges = new List<(int areaI, int areaJ, string colour)>();
        int doorNum = 0;
        foreach (var (i, j) in map.RandomEdges(areaCount, averageDegree))
        {
            var u = map.AreaVertices[i][map.random.Next(0, areaSize)];
            var v = map.AreaVertices[j][map.random.Next(0, areaSize)];
            string colour = $"key_{doorNum++}";
            map.MakeEdge(u, v, colour, 0, 0);
            interAreaEdges.Add((i, j, colour));
        }

        // Find which area contains Start
        int startArea = 0;
        for (int area = 0; area < areaCount && startArea == 0; area++)
        {
            for (int k = 0; k < areaSize; k++)
            {
                if (map.AreaVertices[area][k] == map.Start) { startArea = area; break; }
            }
        }

        // Build area adjacency (colour-labelled)
        var areaAdj = new Dictionary<int, List<(int neighbor, string colour)>>();
        for (int a = 0; a < areaCount; a++) areaAdj[a] = [];
        foreach (var (ai, aj, colour) in interAreaEdges)
        {
            areaAdj[ai].Add((aj, colour));
            areaAdj[aj].Add((ai, colour));
        }

        // BFS expansion: place each door's key in a currently-accessible area so the player
        // can always find the next key they need.
        var accessible = new HashSet<int> { startArea };
        var keysPlaced = new HashSet<string>();

        while (true)
        {
            // Collect frontier edges: accessible area -> not-yet-accessible area
            var frontier = new List<(int src, int dst, string colour)>();
            foreach (int a in accessible)
            {
                foreach (var (n, c) in areaAdj[a])
                {
                    if (!accessible.Contains(n) && !keysPlaced.Contains(c))
                        frontier.Add((a, n, c));
                }
            }

            if (frontier.Count == 0)
                break;

            var (src, dst, colour) = frontier[map.random.Next(frontier.Count)];
            keysPlaced.Add(colour);
            PlaceKeyInAccessibleArea(map, accessible, areaSize, colour);
            accessible.Add(dst);
        }

        // Place keys for any remaining doors (extra cycle edges between already-accessible areas)
        foreach (var (_, _, colour) in interAreaEdges)
        {
            if (!keysPlaced.Contains(colour))
                PlaceKeyInAccessibleArea(map, accessible, areaSize, colour);
        }

        return map;
    }

    private static void PlaceKeyInAccessibleArea(RTBMap map, HashSet<int> accessible, int areaSize, string colour)
    {
        var accList = accessible.ToList();

        // Retry to avoid overwriting an existing key
        for (int attempt = 0; attempt < 1000; attempt++)
        {
            int area = accList[map.random.Next(accList.Count)];
            int idx = map.random.Next(areaSize);
            if (map.AreaVertices[area][idx].KeyColour == null)
            {
                map.AreaVertices[area][idx].KeyColour = colour;
                return;
            }
        }
    }

    public void AddTreasure(int treasureCount) {

        if (treasureCount == 0) { return; }

        var TreasureVertices = new HashSet<(int, int)>();

        void addTreasure(int area, int i)
        {
            TreasureVertices.Add((area, i));
            AreaVertices[area][i].Treasure = true;
            TreasureLocations.Add(AreaVertices[area][i].Name);
        
        }
        // Randomly place treasure, at least one per area unless there are none
        for (int area = 0; area < AreaCount; area++)
        {
            int i = random.Next(0, AreaSize);
            while (TreasureVertices.Contains((area, i))) {
                i = random.Next(0, AreaSize);
            }
            
            addTreasure(area, i);
        }

        while (TreasureVertices.Count < treasureCount)
        {
            int area = random.Next(0, AreaCount);
            int i = random.Next(0, AreaSize);
            
            while (TreasureVertices.Contains((area, i))) {
                i = random.Next(0, AreaSize);
            }            
            addTreasure(area, i);
        }
    }

    public string RandomName() {
        string allowedChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        return string.Create(10, allowedChars, (buffer, chars) => random.GetItems(chars, buffer));
    }

    public void MakeEdge(Location u, Location v, string? keyColour, int toll, int ropeCost)
    {
        string eName1 = RandomName();
        string eName2 = RandomName();

        u.AddSpecialExit(eName1, v, eName2, keyColour, toll, ropeCost);
        v.AddSpecialExit(eName2, u, eName1, keyColour, toll, ropeCost);
    }


    public Location[] CreateArea(int areaSize, int averageDegree) 
    {
        // Create vertices
        var vertices = new Location[areaSize];

        for (int i = 0; i < areaSize; i++)
        {
            vertices[i] = new Location(RandomName());
            Locations.Add(vertices[i].Name, vertices[i]);
        }
        
        // Create edges.  No special mechanics
        foreach (var (i, j) in RandomEdges(areaSize, averageDegree))
        {
            MakeEdge(vertices[i], vertices[j], null, 0, 0);
        }

        return vertices;
    }

    public HashSet<(int, int)> RandomEdges(int n, int averageDegree)
    {
        var edges = new HashSet<(int,int)>();

        // Create a spanning tree to start so that it will be connected
        for (int i = 1; i < n; i++)
        {   
            int j = random.Next(0, i);
            edges.Add((i,j));
        }

        // Clamp averageDegree for small sizes
        int targetEdgeCount = averageDegree * n / 2;
        int maxEdges = n * (n - 1) / 2;
        targetEdgeCount = int.Min(targetEdgeCount, maxEdges);

        // Fill in with edges to get the desired average degree
        for (int edgeCount = n - 1; edgeCount < targetEdgeCount; edgeCount++)
        {   
            int j = random.Next(0, n);
            int i = random.Next(0, n);
            while (i == j || edges.Contains((i,j)) || edges.Contains((j, i)))
            {   
                j = random.Next(0, n);
                i = random.Next(0, n);
            }
            edges.Add((i, j));
        }
        return edges;
    }

    public void PrintMap(string fileName)
    {
        using (var writer = new StreamWriter(fileName)) {
            var edges = new HashSet<(string,string)>();
            writer.WriteLine("graph G {");
            writer.WriteLine("   layout=neato; overlap=false; sep=\"+0.8\"");
            writer.WriteLine($"   \"{Start.Name}\" [style=filled, fillcolor=green, fontcolor=white]");
            writer.WriteLine($"   \"{Egress.Name}\" [style=filled, fillcolor=red, fontcolor=white]");
            foreach (var (n, L) in Locations) {
                foreach (var (e,E) in L.Exits) {
                    var d = E.Destination.Name;
                    if (edges.Contains((d, n))) { continue; }
                    writer.WriteLine($"   \"{n}\" -- \"{E.Destination.Name}\";");
                    edges.Add((n, d));
                }
            }
            writer.WriteLine("}");
        }
    }
}