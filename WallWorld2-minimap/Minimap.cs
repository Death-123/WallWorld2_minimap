using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using World;
using Object = UnityEngine.Object;

namespace WallWorld2_minimap;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Minimap : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;
    public static Minimap instance;
    private static AssetBundle assetBundle;
    private static GameObject canvasPrefab;
    private static GameObject blockPrefab;
    private GameObject canvas;
    private WorldEntity worldEntity;
    private PlayerEntity playerEntity;
    private Transform playerDot;
    private int BLOCK_SIZE = 8;
    private int MAP_OFFSET = -64;
    private Color colorPlayer = new Color32(220, 170, 0, 200);
    private Color colorEntrance = new Color32(0, 150, 0, 200);
    private Color colorExit = new Color32(0, 150, 150, 200);
    private Color colorRelic = new Color32(0, 80, 180, 200);
    private Color colorWow = new Color32(140, 40, 40, 200);
    private bool enable = true;
    private bool ingame;
    private bool mapInit;
    private DungeonBranchEntity dungeon;
    private Text relicText;
    private Text wowText;
    private string strWow;
    private Transform mainPanel;
    private float updateTimer = 0f;
    private static ConfigEntry<KeyboardShortcut> EnableKey;

    private enum BlockTypes
    {
        BorderBlock,
        CommonBlock,
        ResourceBlock,
        GeyserBlock,
        SpikyBlock,
        HealBlock,
        TeleportBlock,
        BoosterBlock,
        TechnoBlock,
        GraniteBlock,
        LightBlock,
        ExplosionBlock,
        SpecialBlock,
    }

    private static Dictionary<BlockTypes, Transform> Blocks = new();
    private static Dictionary<BlockTypes, Color> BlockColors = new();

    public static AssetBundle LoadAssetBundle(string filename)
    {
        var callingAssembly = Assembly.GetCallingAssembly();
        return AssetBundle.LoadFromStream(callingAssembly.GetManifestResourceStream(callingAssembly.GetName().Name + "." + filename));
    }

    private static void LoadAssets()
    {
        assetBundle = LoadAssetBundle("Minimap.ww");
        canvasPrefab = assetBundle.LoadAsset<GameObject>("TestPrefab");
        blockPrefab = assetBundle.LoadAsset<GameObject>("Block");
    }

    private void FindWorldEntity()
    {
        var gizmosRender = GameObject.Find("GizmosUtils")?.GetComponent<GizmosRender>();
        worldEntity = Traverse.Create(gizmosRender).Field("IWorld").GetValue() as WorldEntity;
        playerEntity = Traverse.Create(worldEntity).Field("player").GetValue() as PlayerEntity;
    }

    private void Awake()
    {
        // Plugin startup logic
        Logger = base.Logger;
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

        EnableKey = Config.Bind("Keys", "EnableKey", new KeyboardShortcut(KeyCode.O), "启用\nEnable");
        BlockColors.Add(BlockTypes.BorderBlock, new Color32(100, 100, 100, 200));
        BlockColors.Add(BlockTypes.CommonBlock, new Color32(60, 10, 10, 200));
        BlockColors.Add(BlockTypes.ResourceBlock, new Color32(250, 250, 250, 200));
        BlockColors.Add(BlockTypes.GeyserBlock, new Color32(250, 20, 20, 200));
        BlockColors.Add(BlockTypes.SpikyBlock, new Color32(200, 20, 20, 200));
        BlockColors.Add(BlockTypes.HealBlock, new Color32(20, 200, 20, 200));
        BlockColors.Add(BlockTypes.TeleportBlock, new Color32(20, 200, 200, 200));
        BlockColors.Add(BlockTypes.BoosterBlock, new Color32(150, 150, 20, 200));
        BlockColors.Add(BlockTypes.TechnoBlock, new Color32(200, 20, 200, 200));
        BlockColors.Add(BlockTypes.GraniteBlock, new Color32(150, 150, 200, 200));
        BlockColors.Add(BlockTypes.LightBlock, new Color32(250, 250, 20, 200));
        BlockColors.Add(BlockTypes.ExplosionBlock, new Color32(250, 20, 250, 200));
        BlockColors.Add(BlockTypes.SpecialBlock, new Color32(200, 100, 20, 200));

        instance = this;
        LoadAssets();
        for (var i = 0; i < canvasPrefab.transform.GetChild(0).childCount; i++)
        {
            var child = canvasPrefab.transform.GetChild(0).GetChild(i);
            child.localPosition += new Vector3(200, 0);
        }
        foreach (BlockTypes blockType in Enum.GetValues(typeof(BlockTypes)))
        {
            Blocks.Add(blockType, CreateBlock(BlockColors.TryGetValue(blockType, out var color) ? color : BlockColors[BlockTypes.SpecialBlock]));
        }

        SceneManager.sceneLoaded += SceneCheck;
    }

    private void SceneCheck(Scene scene, LoadSceneMode loadMode)
    {
        if (scene.name != "Game" && scene.name != "Game_Hole")
        {
            ingame = false;
            if (canvas) Destroy(canvas);
            canvas = null;
            return;
        }

        ingame = true;
        FindWorldEntity();
        if (!canvas) canvas = Instantiate(canvasPrefab);
        canvas.name = "MiniMap";
        mainPanel = canvas.transform.GetChild(0).GetChild(1);
        playerDot = mainPanel.GetChild(mainPanel.transform.childCount - 1);
        playerDot.GetComponent<Image>().color = colorPlayer;
    }

    private static Transform CreateBlock(Color32 color)
    {
        var block = Instantiate(canvasPrefab.transform.GetChild(0).GetChild(2), instance.transform);
        block.GetComponent<Image>().color = color;
        return block;
    }

    private Transform CreateBlock(BlockTypes type, float x, float y)
    {
        if (!mainPanel) return null;
        var block = Instantiate(Blocks[type], mainPanel);
        block.gameObject.name = $"{type.ToString()}_{x}_{y}";
        block.localPosition = new Vector3(x * BLOCK_SIZE + MAP_OFFSET, y * BLOCK_SIZE + MAP_OFFSET, 0);
        return block;
    }

    private Transform CreateBlock(BlockTypes type, Vector3 position)
    {
        return CreateBlock(type, position.x, position.y);
    }

    private bool dungeonInit;

    private void InitDungeon()
    {
        if(dungeon == null) return;
        CleanMap();
        foreach (var position in dungeon.UnbreakableBlocksMeta)
        {
            CreateBlock(BlockTypes.BorderBlock, position.x, position.y);
        }

        dungeonInit = true;
    }

    private void CleanMap(string name = "all")
    {
        if (!mainPanel) return;
        foreach (Transform child in mainPanel)
        {
            if (child.tag == "Player") continue;
            if (name == "all" ||
                name.StartsWith("!") && !child.name.StartsWith(name.Substring(1)) ||
                child.name.StartsWith(name)
               ) Destroy(child.gameObject);
        }
    }

    private HashSet<string> nameSet = [];

    private void Update()
    {
        if (EnableKey.Value.IsDown()) enable = !enable;
        if (!enable || !ingame)
        {
            CleanMap();
            dungeonInit = false;
            return;
        }

        updateTimer += Time.deltaTime;
        if (updateTimer < 0.3f) return;
        updateTimer = 0f;
        
        if (worldEntity?.CurrentBranch != dungeon) dungeonInit = false;
        dungeon = worldEntity?.CurrentBranch;
        
        if (worldEntity == null || dungeon == null)
        {
            playerDot.localPosition = new Vector3(-99999f, -99999f, 0.0f);
            CleanMap();
            return;
        }

        var heroPos = playerEntity?.HeroEntity.Position;
        if (heroPos == null) return;
        var minX = Math.Min(dungeon.StartPosition.x, dungeon.EndPosition.x);
        var maxX = Math.Max(dungeon.StartPosition.x, dungeon.EndPosition.x);
        var minY = Math.Min(dungeon.StartPosition.y, dungeon.EndPosition.y);
        var maxY = Math.Max(dungeon.StartPosition.y, dungeon.EndPosition.y);
        if (worldEntity.Direction.x == 1)
        {
            minY = -minY;
            maxY = -maxY;
        }
        if (heroPos.Value.x <  minX - 5 || heroPos.Value.x > maxX + 5 ||heroPos.Value.y <  minY - 5 || heroPos.Value.y > maxY + 5)
        {
            dungeon = null;
            return;
        }
        if (!dungeonInit) InitDungeon();
        CleanMap("!Border");
        playerDot.localPosition = new Vector3((heroPos.Value.x - 0.5f) * BLOCK_SIZE + MAP_OFFSET, (heroPos.Value.y - dungeon.StartPosition.y) * BLOCK_SIZE + MAP_OFFSET);

        //所有方块
        foreach (var position in dungeon.PlacedBlocksMeta)
        {
            dungeon.TryGetBlock(position, out var blockEntity);
            if (blockEntity == null) continue;
            if (nameSet.Add(blockEntity.Description.name)) Logger.LogInfo(blockEntity.Description.name);
            var blockType = BlockTypes.BorderBlock;
            if (blockEntity.Description.name != "UnbreakableBlockDescription")
            {
                foreach (var type in Enum.GetNames(typeof(BlockTypes)))
                {
                    if (blockEntity.Description.name == type)
                    {
                        blockType = (BlockTypes)Enum.Parse(typeof(BlockTypes), type);
                    }
                }

                if (blockType == BlockTypes.BorderBlock)
                {
                    if (!blockEntity.IsBreakable)
                        blockType = BlockTypes.SpecialBlock;
                    else
                        blockType = blockEntity.ContainsResource ? BlockTypes.ResourceBlock : BlockTypes.CommonBlock;
                }
            }

            if (blockType != BlockTypes.BorderBlock) CreateBlock(blockType, position);
        }

        //掉落的资源
        if (Traverse.Create(dungeon).Field("WorldResourceEntity").GetValue() is WorldResourceEntity worldResourceEntity)
        {
            var resSet = new HashSet<Vector3Int>();
            foreach (var resource in worldResourceEntity.LevelResources.Where(resource => resource.OwnerDungeon == dungeon))
            {
                resSet.Add(new Vector3Int((int)resource.Position.x, (int)(resource.Position.y + 0.5 - dungeon.StartPosition.y)));
            }

            foreach (var pos in resSet)
            {
                CreateBlock(BlockTypes.ResourceBlock, pos);
            }
        }
    }

    private void test1()
    {
        if (worldEntity.CurrentBranch != null && worldEntity.CurrentBranch != this.dungeon)
        {
            this.dungeon = worldEntity.CurrentBranch;
            foreach (Transform transform in this.mainPanel.transform)
            {
                if (!(transform.tag == "Player"))
                    Destroy(transform.gameObject);
            }

            Text component = canvas.transform.GetChild(0).GetChild(0).GetComponent<Text>();
            this.relicText = canvas.transform.GetChild(0).GetChild(6).GetComponent<Text>();
            this.wowText = canvas.transform.GetChild(0).GetChild(7).GetComponent<Text>();
            string str = "Biome: " + this.dungeon.BiomeMetadata.Name;
            component.text = str;
            this.relicText.color = Color.grey;
            this.wowText.color = Color.grey;
            this.relicText.text = "Relic: -";
            this.strWow = "Rooms: ";
            if (this.dungeon.BiomeMetadata.IsContainsRelic)
                this.relicText.color = colorRelic;
            if (this.dungeon.BiomeMetadata.IsContainsWowLocation)
            {
                this.wowText.color = colorWow;
                this.strWow += this.dungeon.BiomeMetadata.WowLocationRelicName;
            }

            this.wowText.text = this.strWow;
            mapInit = false;
        }

        if (!((Object)this.mainPanel != null))
            return;


        if (mapInit)
            return;
        Transform child = canvas.transform.GetChild(0).GetChild(2);
        canvas.transform.GetChild(0).GetChild(3);
        if (worldEntity.CurrentBranch == null || worldEntity.CurrentBranch.BiomeMetadata == null || worldEntity.CurrentBranch.BiomeMetadata.Zones == null)
            return;
        Transform transform1 = Instantiate(child, this.mainPanel);
        transform1.name = "Start";
        transform1.transform.localPosition = new Vector3(-64f, worldEntity.CurrentBranch.BiomeMetadata.StartPosition * 8 - 64, 0.0f);
        Transform transform2 = Instantiate(child, this.mainPanel);
        transform2.name = "End";
        transform2.transform.localPosition = new Vector3(worldEntity.CurrentBranch.BiomeMetadata.ExitPos.x * 8 - 64, worldEntity.CurrentBranch.BiomeMetadata.ExitPos.y * 8 - 64, 0.0f);
        bool isBreakableOnly = false;
        foreach (ZoneMetadata zone in worldEntity.CurrentBranch.BiomeMetadata.Zones)
        {
            if (zone.Blocks == null) continue;
            foreach (BlockMetadata block1 in zone.Blocks)
            {
                if (block1 == null) continue;
                foreach (Vector2Int position in block1.Positions)
                {
                    BlockEntity block2;
                    worldEntity.CurrentBranch.TryGetBlock(new Vector3Int(position.x, position.y + worldEntity.CurrentBranch.BiomeMetadata.StartPosition), out block2, isBreakableOnly);
                    if (block2 == null) continue;
                    Transform transform3 = Instantiate(child, this.mainPanel);
                    if (block2.IsBreakable)
                    {
                        if (block2.ContainsResource)
                        {
                            if (block2.Resource is { Weight: > 1 })
                                this.relicText.text = "Relic: " + block2.Resource.Name;
                        }
                    }
                    else

                        transform3.transform.localPosition = new Vector3(position.x * 8 - 64, position.y * 8 - 64, 0.0f);
                }
            }

            mapInit = true;
        }
    }

    private void OnDestroy()
    {
        if (canvas) Destroy(canvas);
        Destroy(canvasPrefab);
        Destroy(blockPrefab);
        Destroy(assetBundle);
        SceneManager.sceneLoaded -= SceneCheck;
    }
}