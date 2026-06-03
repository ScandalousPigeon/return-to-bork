int runs = 10;
int averageDegree = 3;
int startCoins = 10000000;
int startRope = 10000000;
int actionLimit = int.MaxValue; // no practical limit on actions

// Maze A
int areaSize = 1000;
int areaCount = 1;
int treasureCount = 0;
string mapType = "Maze";
string challengeType = "A";
RunTrials();

// Maze 100
challengeType = "100";
treasureCount = 2 * areaCount;

RunTrials();

// Trolls A
mapType = "Trolls";
challengeType = "A";
treasureCount = 0;
areaSize = 50;
areaCount = 20;
RunTrials();

// Trolls 100
challengeType = "100";
treasureCount = 2 * areaCount;
RunTrials();

// Keys A
mapType = "Keys";
challengeType = "A";
RunTrials();

// Ropes 100
mapType = "Ropes";
challengeType = "100";
treasureCount = 2 * areaCount;
RunTrials();

void RunTrials()
{
    Console.WriteLine($"----------{mapType}--{challengeType}------------");
    for (int seed = 0; seed < runs; seed++)
    {
        RTBMap map;
        switch (mapType) {
            case "Keys":
                map = RTBMap.MapWithKeys(seed, areaSize, areaCount, averageDegree);
                break;
            case "Ropes":
                map = RTBMap.MapWithRopes(seed, areaSize, areaCount, averageDegree, treasureCount);
                break;
            default: 
                map = RTBMap.MapWithTolls(seed, areaSize, areaCount, averageDegree, treasureCount);
                break;
        }
        // Uncomment to write the map to a file in GraphViz format
        // map.PrintMap($"{mapType}-{challengeType}-{seed}-{areaSize}-{areaCount}-{averageDegree}-{treasureCount}.gv");

        var player = new RTBPlayer(mapType, challengeType);
        var rtb = new ReturnToBork(map, player, startCoins, startRope, actionLimit);
        Console.Write($"Seed {seed} ");
        rtb.Learn();
        rtb.Challenge();
    }
}