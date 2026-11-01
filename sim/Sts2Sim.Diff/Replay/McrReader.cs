using Sts2Sim.Core.Run;
using CoreRelicRarity = Sts2Sim.Core.RelicRarity;
using CoreMapPointType = Sts2Sim.Core.MapPointType;
using CoreRoomType = Sts2Sim.Core.RoomType;

namespace Sts2Sim.Diff.Replay;

public static class McrReader
{
    private const int MaxGameMode = 3;
    private const int MaxMapPointType = 8;
    private const int MaxRoomType = 8;
    private const int MaxRelicRarity = 7;
    private const int MaxRunRngType = 11;
    private const int MaxPlayerRngType = 2;
    private const int MaxCardCreationSource = 3;
    private const int MaxCardRarityOddsType = 5;
    private const int MaxDynamicVarType = 5;
    private const int MaxPlayerTurnPhase = 5;
    private const int MaxCardKeyword = 7;
    private const int MaxPlayerChoiceType = 6;
    private const int MaxGameActionType = 4;

    private static readonly string[] NetActionTypesByName =
    {
        "NetConsoleCmdGameAction",
        "NetDiscardPotionGameAction",
        "NetEndPlayerTurnAction",
        "NetMoveToMapCoordAction",
        "NetPickRelicAction",
        "NetPlayCardAction",
        "NetReadyToBeginEnemyTurnAction",
        "NetUndoEndPlayerTurnAction",
        "NetUsePotionAction",
        "NetVoteForMapCoordAction",
        "NetVoteToMoveToNextActAction"
    };

    public static McrReplay ReadFile(string path, McrModelNetIds? ids = null) => Read(File.ReadAllBytes(path), ids);

    public static McrReplay Read(byte[] bytes, McrModelNetIds? ids = null)
    {
        var r = new McrBitReader(bytes);
        string version = r.ReadString();
        string gitCommit = r.ReadString();
        uint hash = r.ReadUInt();
        bool verified;
        if (ids != null)
        {
            verified = ids.Hash == hash;
        }
        else
        {
            ids = McrModelNetIds.ForHash(hash);
            verified = ids != null;
            ids ??= McrModelNetIds.ForVersion(version);
        }
        if (ids == null)
        {
            throw new McrFormatException($"replay {version} {gitCommit}: modelIdHash {hash} matches no known model net-id table and no table covers that version");
        }
        r.Ids = ids;
        var choiceIds = new List<uint>();
        int nChoice = r.ReadCount();
        for (int i = 0; i < nChoice; i++)
        {
            choiceIds.Add(r.ReadUInt());
        }
        var rewardIds = new List<int>();
        int nReward = r.ReadCount();
        for (int i = 0; i < nReward; i++)
        {
            rewardIds.Add(r.ReadInt());
        }
        uint nextActionId = r.ReadUInt();
        uint nextChecksumId = r.ReadUInt();
        uint nextHookId = r.ReadUInt();
        var (run, mode) = ReadRun(r);
        var events = r.ReadList(ReadEvent);
        var checksums = r.ReadList(ReadChecksum);
        int trailing = r.RemainingBits;
        if (trailing >= 8)
        {
            throw new McrFormatException($"{trailing} unread bits after checksum data");
        }
        return new McrReplay
        {
            Version = version,
            GitCommit = gitCommit,
            ModelIdHash = hash,
            Table = ids,
            HashVerified = verified,
            ChoiceIds = choiceIds,
            RewardIds = rewardIds,
            NextActionId = nextActionId,
            NextChecksumId = nextChecksumId,
            NextHookId = nextHookId,
            Run = run,
            GameMode = mode,
            Events = events,
            Checksums = checksums,
            TrailingBits = trailing
        };
    }

    private static string Snake(string name) => Sts2Sim.Core.Random.DeterministicHash.SnakeCase(name);

    private static (SavedRun, McrGameMode) ReadRun(McrBitReader r)
    {
        int schema = r.ReadInt();
        var acts = r.ReadList(ReadAct);
        var modifiers = r.ReadList(ReadModifier);
        long? dailyTime = r.ReadBool() ? r.ReadLong() : null;
        var mode = (McrGameMode)r.ReadEnum(MaxGameMode);
        int actIndex = r.ReadInt(4);
        var eventsSeen = NonNull(r.ReadModelEntryList());
        SavedRoom? preFinished = r.ReadBool() ? ReadRoom(r) : null;
        var odds = new SavedRunOdds
        {
            UnknownMapPointMonsterOddsValue = r.ReadFloat(),
            UnknownMapPointEliteOddsValue = r.ReadFloat(),
            UnknownMapPointTreasureOddsValue = r.ReadFloat(),
            UnknownMapPointShopOddsValue = r.ReadFloat()
        };
        var players = r.ReadList(ReadPlayer);
        var rng = ReadRunRng(r);
        var sharedBag = ReadGrabBag(r);
        var visited = r.ReadList(ReadCoord);
        int nActs = r.ReadCount();
        var history = new List<List<SavedMapPointHistoryEntry>>(nActs);
        for (int i = 0; i < nActs; i++)
        {
            history.Add(r.ReadList(ReadHistoryEntry));
        }
        r.ReadLong();
        r.ReadLong();
        r.ReadLong();
        r.ReadLong();
        int ascension = r.ReadInt(8);
        if (r.ReadBool())
        {
            SkipMapDrawings(r);
        }
        var extra = new SavedExtraRunFields
        {
            StartedWithNeow = r.ReadBool(),
            TestSubjectKills = r.ReadInt(),
            FreedRepy = r.ReadBool()
        };
        r.ReadInt();
        var run = new SavedRun
        {
            SchemaVersion = schema,
            Acts = acts,
            Modifiers = modifiers,
            DailyTime = dailyTime,
            CurrentActIndex = actIndex,
            EventsSeen = eventsSeen,
            PreFinishedRoom = preFinished,
            Odds = odds,
            SharedRelicGrabBag = sharedBag,
            Players = players,
            Rng = rng,
            VisitedMapCoords = visited,
            MapPointHistory = history,
            Ascension = ascension,
            GameMode = Snake(mode.ToString()),
            ExtraFields = extra
        };
        return (run, mode);
    }

    private static List<string> NonNull(List<string?> list) => list.Select(x => x ?? McrModelNetIds.NoneEntry).ToList();

    private static MapCoord ReadCoord(McrBitReader r)
    {
        int col = r.ReadByte();
        int row = r.ReadByte();
        return new MapCoord(col, row);
    }

    private static SavedAct ReadAct(McrBitReader r)
    {
        string id = r.ReadModelEntry() ?? McrModelNetIds.NoneEntry;
        var rooms = new SavedRoomSet
        {
            EventIds = NonNull(r.ReadModelEntryList()),
            EventsVisited = r.ReadInt(),
            NormalEncounterIds = NonNull(r.ReadModelEntryList()),
            NormalEncountersVisited = r.ReadInt(),
            EliteEncounterIds = NonNull(r.ReadModelEntryList()),
            EliteEncountersVisited = r.ReadInt(),
            BossEncountersVisited = r.ReadInt(),
            BossId = r.ReadBool() ? r.ReadModelEntry() : null,
            SecondBossId = r.ReadBool() ? r.ReadModelEntry() : null,
            AncientId = r.ReadBool() ? r.ReadModelEntry() : null
        };
        SavedActMapData? map = r.ReadBool() ? ReadActMap(r) : null;
        return new SavedAct { Id = id, Rooms = rooms, SavedMap = map };
    }

    private static SavedActMapData ReadActMap(McrBitReader r)
    {
        int width = r.ReadInt(8);
        int height = r.ReadInt(8);
        var boss = ReadMapPoint(r);
        var start = ReadMapPoint(r);
        SavedMapPoint? second = r.ReadBool() ? ReadMapPoint(r) : null;
        int n = r.ReadCount(16);
        var points = new List<SavedMapPoint>(n);
        for (int i = 0; i < n; i++)
        {
            points.Add(ReadMapPoint(r));
        }
        int nStart = r.ReadCount(8);
        var startCoords = new List<MapCoord>(nStart);
        for (int i = 0; i < nStart; i++)
        {
            startCoords.Add(ReadCoord(r));
        }
        return new SavedActMapData
        {
            Width = width,
            Height = height,
            Boss = boss,
            Start = start,
            SecondBoss = second,
            Points = points,
            StartCoords = startCoords
        };
    }

    private static SavedMapPoint ReadMapPoint(McrBitReader r)
    {
        var coord = ReadCoord(r);
        int type = r.ReadInt(8);
        bool canModify = r.ReadBool();
        int n = r.ReadCount(8);
        var children = new List<MapCoord>(n);
        for (int i = 0; i < n; i++)
        {
            children.Add(ReadCoord(r));
        }
        return new SavedMapPoint { Coord = coord, Type = (CoreMapPointType)type, CanModify = canModify, Children = children };
    }

    private static SavedModifier ReadModifier(McrBitReader r)
    {
        string id = r.ReadModelEntry() ?? McrModelNetIds.NoneEntry;
        SavedProperties? props = r.ReadBool() ? ReadProps(r) : null;
        return new SavedModifier { Id = id, Props = props };
    }

    public static SavedProperties ReadProps(McrBitReader r)
    {
        var ints = new List<KeyValuePair<string, int>>();
        var intArrays = new List<KeyValuePair<string, int[]>>();
        var bools = new List<KeyValuePair<string, bool>>();
        var modelIds = new List<KeyValuePair<string, string>>();
        var cards = new List<KeyValuePair<string, SavedCard>>();
        var cardArrays = new List<KeyValuePair<string, SavedCard[]>>();
        var strings = new List<KeyValuePair<string, string>>();
        if (r.ReadBool())
        {
            int n = r.ReadCount(8);
            for (int i = 0; i < n; i++)
            {
                string name = r.ReadPropertyName();
                ints.Add(new(name, r.ReadInt()));
            }
        }
        if (r.ReadBool())
        {
            int n = r.ReadCount(8);
            for (int i = 0; i < n; i++)
            {
                string name = r.ReadPropertyName();
                int len = r.ReadCount();
                var arr = new int[len];
                for (int k = 0; k < len; k++)
                {
                    arr[k] = r.ReadInt();
                }
                intArrays.Add(new(name, arr));
            }
        }
        if (r.ReadBool())
        {
            int n = r.ReadCount(8);
            for (int i = 0; i < n; i++)
            {
                string name = r.ReadPropertyName();
                bools.Add(new(name, r.ReadBool()));
            }
        }
        if (r.ReadBool())
        {
            int n = r.ReadCount(8);
            for (int i = 0; i < n; i++)
            {
                string name = r.ReadPropertyName();
                modelIds.Add(new(name, r.ReadFullModelId() ?? "NONE.NONE"));
            }
        }
        if (r.ReadBool())
        {
            int n = r.ReadCount(8);
            for (int i = 0; i < n; i++)
            {
                string name = r.ReadPropertyName();
                cards.Add(new(name, ReadCard(r)));
            }
        }
        if (r.ReadBool())
        {
            int n = r.ReadCount(8);
            for (int i = 0; i < n; i++)
            {
                string name = r.ReadPropertyName();
                int len = r.ReadCount();
                var arr = new SavedCard[len];
                for (int k = 0; k < len; k++)
                {
                    arr[k] = ReadCard(r);
                }
                cardArrays.Add(new(name, arr));
            }
        }
        if (r.ReadBool())
        {
            int n = r.ReadCount(8);
            for (int i = 0; i < n; i++)
            {
                string name = r.ReadPropertyName();
                strings.Add(new(name, r.ReadString()));
            }
        }
        return new SavedProperties
        {
            Ints = ints,
            IntArrays = intArrays,
            Bools = bools,
            ModelIds = modelIds,
            Cards = cards,
            CardArrays = cardArrays,
            Strings = strings
        };
    }

    public static SavedCard ReadCard(McrBitReader r)
    {
        string id = r.ReadModelEntry() ?? McrModelNetIds.NoneEntry;
        int level = r.ReadInt(8);
        SavedEnchantment? ench = null;
        if (r.ReadBool())
        {
            string eid = r.ReadModelEntry() ?? McrModelNetIds.NoneEntry;
            int amount = r.ReadInt(8);
            SavedProperties? eprops = r.ReadBool() ? ReadProps(r) : null;
            ench = new SavedEnchantment { Id = eid, Amount = amount, Props = eprops };
        }
        SavedProperties? props = r.ReadBool() ? ReadProps(r) : null;
        int? floor = r.ReadBool() ? r.ReadInt(8) : null;
        return new SavedCard { Id = id, CurrentUpgradeLevel = level, Enchantment = ench, Props = props, FloorAddedToDeck = floor };
    }

    public static SavedRelic ReadRelic(McrBitReader r)
    {
        string id = r.ReadModelEntry() ?? McrModelNetIds.NoneEntry;
        SavedProperties? props = r.ReadBool() ? ReadProps(r) : null;
        int? floor = r.ReadBool() ? r.ReadInt(8) : null;
        return new SavedRelic { Id = id, Props = props, FloorAddedToDeck = floor };
    }

    private static SavedPotion ReadPotion(McrBitReader r)
    {
        string id = r.ReadModelEntry() ?? McrModelNetIds.NoneEntry;
        int slot = r.ReadInt(4);
        return new SavedPotion { Id = id, SlotIndex = slot };
    }

    private static RngSnapshot ReadRng(McrBitReader r)
    {
        int counter = r.ReadInt();
        ulong s0 = r.ReadULong();
        ulong s1 = r.ReadULong();
        ulong s2 = r.ReadULong();
        ulong s3 = r.ReadULong();
        return new RngSnapshot(counter, s0, s1, s2, s3);
    }

    public static SavedRunRng ReadRunRng(McrBitReader r)
    {
        string seed = r.ReadString();
        int n = r.ReadCount(8);
        var rngs = new Dictionary<string, RngSnapshot>(StringComparer.Ordinal);
        for (int i = 0; i < n; i++)
        {
            var type = (McrRunRngType)r.ReadEnum(MaxRunRngType);
            rngs[Snake(type.ToString())] = r.Legacy ? new RngSnapshot(r.ReadInt(), 0, 0, 0, 0) : ReadRng(r);
        }
        return new SavedRunRng { Seed = seed, Rngs = rngs };
    }

    public static SavedPlayerRng ReadPlayerRng(McrBitReader r)
    {
        ulong seed = r.Legacy ? r.ReadUInt() : r.ReadULong();
        int n = r.ReadCount(8);
        var rngs = new Dictionary<string, RngSnapshot>(StringComparer.Ordinal);
        for (int i = 0; i < n; i++)
        {
            var type = (McrPlayerRngType)r.ReadEnum(MaxPlayerRngType);
            rngs[Snake(type.ToString())] = r.Legacy ? new RngSnapshot(r.ReadInt(), 0, 0, 0, 0) : ReadRng(r);
        }
        return new SavedPlayerRng { Seed = seed, Rngs = rngs };
    }

    public static SavedRelicGrabBag ReadGrabBag(McrBitReader r)
    {
        int n = r.ReadCount();
        var lists = new Dictionary<CoreRelicRarity, List<string>>();
        for (int i = 0; i < n; i++)
        {
            var rarity = (CoreRelicRarity)r.ReadEnum(MaxRelicRarity);
            lists[rarity] = NonNull(r.ReadModelEntryList());
        }
        return new SavedRelicGrabBag { RelicIdLists = lists };
    }

    private static SavedPlayer ReadPlayer(McrBitReader r)
    {
        ulong netId = r.ReadULong();
        string character = r.ReadModelEntry() ?? McrModelNetIds.NoneEntry;
        int hp = r.ReadInt();
        int maxHp = r.ReadInt();
        int maxEnergy = r.ReadInt(16);
        int potionSlots = r.ReadInt(8);
        int gold = r.ReadInt();
        int orbSlots = r.ReadInt(16);
        var deck = r.ReadList(ReadCard);
        var relics = r.ReadList(ReadRelic);
        var potions = r.ReadList(ReadPotion);
        var rng = ReadPlayerRng(r);
        var odds = new SavedPlayerOdds { CardRarityOddsValue = r.ReadFloat(), PotionRewardOddsValue = r.ReadFloat() };
        var bag = ReadGrabBag(r);
        var extra = new SavedExtraPlayerFields { CardShopRemovalsUsed = r.ReadInt(), WongoPoints = r.ReadInt() };
        r.ReadBool();
        r.ReadInt();
        r.ReadInt();
        int nEpochs = r.ReadCount();
        var epochs = new List<string>(nEpochs);
        for (int i = 0; i < nEpochs; i++)
        {
            epochs.Add(r.ReadEpochId());
        }
        var encountersSeen = NonNull(r.ReadModelEntryList());
        int numberOfRuns = r.ReadInt(16);
        r.ReadFullModelIdList();
        r.ReadFullModelIdList();
        int nDiscEpochs = r.ReadCount();
        for (int i = 0; i < nDiscEpochs; i++)
        {
            r.ReadEpochId();
        }
        r.ReadFullModelIdList();
        r.ReadFullModelIdList();
        return new SavedPlayer
        {
            NetId = netId,
            CharacterId = character,
            CurrentHp = hp,
            MaxHp = maxHp,
            MaxEnergy = maxEnergy,
            MaxPotionSlotCount = potionSlots,
            Gold = gold,
            BaseOrbSlotCount = orbSlots,
            Deck = deck,
            Relics = relics,
            Potions = potions,
            Rng = rng,
            Odds = odds,
            RelicGrabBag = bag,
            ExtraFields = extra,
            UnlockState = new SavedUnlockState { UnlockedEpochs = epochs, EncountersSeen = encountersSeen, NumberOfRuns = numberOfRuns }
        };
    }

    private static SavedRoom ReadRoom(McrBitReader r)
    {
        var roomType = (CoreRoomType)r.ReadInt();
        string? encounter = r.ReadBool() ? r.ReadModelEntry() : null;
        string? evt = r.ReadBool() ? r.ReadModelEntry() : null;
        float gold = r.ReadFloat();
        bool preFinished = r.ReadBool();
        int nExtra = r.ReadCount();
        var extra = new Dictionary<ulong, List<SavedReward>>();
        for (int i = 0; i < nExtra; i++)
        {
            ulong key = r.ReadULong();
            extra[key] = r.ReadList(ReadReward);
        }
        string? parent = r.ReadBool() ? r.ReadModelEntry() : null;
        bool resume = r.ReadBool();
        int nState = r.ReadCount();
        var state = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < nState; i++)
        {
            string k = r.ReadString();
            state[k] = r.ReadString();
        }
        return new SavedRoom
        {
            RoomType = roomType,
            EncounterId = encounter,
            EventId = evt,
            GoldProportion = gold,
            IsPreFinished = preFinished,
            ExtraRewards = extra,
            ParentEventId = parent,
            ShouldResumeParentEvent = resume,
            EncounterState = state
        };
    }

    private static SavedReward ReadReward(McrBitReader r)
    {
        var type = (McrRewardType)r.ReadInt();
        string? predetermined = r.ReadFullModelId();
        SavedCard? special = type == McrRewardType.SpecialCard ? ReadCard(r) : null;
        int goldAmount = r.ReadInt();
        bool stolen = r.ReadBool();
        var source = (McrCardCreationSource)r.ReadEnum(MaxCardCreationSource);
        var odds = (McrCardRarityOddsType)r.ReadEnum(MaxCardRarityOddsType);
        var pools = r.ReadModelEntryList();
        int optionCount = r.ReadInt();
        string? encounterSource = r.ReadModelEntry();
        var json = new JsonObject
        {
            ["reward_type"] = Snake(type.ToString()),
            ["predetermined_model_id"] = predetermined,
            ["special_card"] = special?.Id,
            ["gold_amount"] = goldAmount,
            ["was_gold_stolen_back"] = stolen,
            ["source"] = Snake(source.ToString()),
            ["rarity_odds"] = Snake(odds.ToString()),
            ["card_pool_ids"] = new JsonArray(pools.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray()),
            ["option_count"] = optionCount,
            ["custom_description_encounter_source_id"] = encounterSource
        };
        return new SavedReward { Json = json.ToJsonString() };
    }

    private static SavedMapPointHistoryEntry ReadHistoryEntry(McrBitReader r)
    {
        var type = (CoreMapPointType)r.ReadEnum(MaxMapPointType);
        var rooms = r.ReadList(ReadHistoryRoom);
        var stats = r.ReadList(ReadPlayerHistory);
        return new SavedMapPointHistoryEntry { MapPointType = type, Rooms = rooms, GoldGained = stats.Count > 0 ? stats[0] : null };
    }

    private static SavedMapPointRoom ReadHistoryRoom(McrBitReader r)
    {
        var type = (CoreRoomType)r.ReadEnum(MaxRoomType);
        string? model = r.ReadBool() ? r.ReadFullModelId() : null;
        var monsters = NonNull(r.ReadModelEntryList());
        int turns = r.ReadInt();
        return new SavedMapPointRoom { RoomType = type, ModelId = model != null ? RunSaveReader.StripCategory(model) : null, MonsterIds = monsters, TurnsTaken = turns };
    }

    private static int ReadPlayerHistory(McrBitReader r)
    {
        r.ReadULong();
        int goldGained = r.ReadInt();
        for (int i = 0; i < 11; i++)
        {
            r.ReadInt();
        }
        r.ReadList(ReadEventOption);
        r.ReadList(ReadAncientChoice);
        r.ReadList(ReadCard);
        int nCardChoices = r.ReadCount();
        for (int i = 0; i < nCardChoices; i++)
        {
            ReadCard(r);
            r.ReadBool();
        }
        for (int list = 0; list < 2; list++)
        {
            int n = r.ReadCount();
            for (int i = 0; i < n; i++)
            {
                r.ReadModelEntry();
                r.ReadBool();
            }
        }
        r.ReadModelEntryList();
        r.ReadModelEntryList();
        r.ReadList(ReadCard);
        r.ReadModelEntryList();
        r.ReadList(rr =>
        {
            ReadCard(rr);
            rr.ReadModelEntry();
            return 0;
        });
        r.ReadList(rr =>
        {
            ReadCard(rr);
            ReadCard(rr);
            return 0;
        });
        r.ReadModelEntryList();
        r.ReadModelEntryList();
        r.ReadList(ReadEventOption);
        int nRest = r.ReadCount();
        for (int i = 0; i < nRest; i++)
        {
            r.ReadString();
        }
        r.ReadModelEntryList();
        r.ReadModelEntryList();
        r.ReadModelEntryList();
        r.ReadModelEntryList();
        if (!r.Legacy)
        {
            r.ReadBool();
        }
        return goldGained;
    }

    private static int ReadEventOption(McrBitReader r)
    {
        r.ReadString();
        r.ReadString();
        if (r.ReadBool())
        {
            int n = r.ReadCount();
            for (int i = 0; i < n; i++)
            {
                r.ReadString();
                var type = (McrDynamicVarType)r.ReadEnum(MaxDynamicVarType);
                switch (type)
                {
                    case McrDynamicVarType.DynamicString:
                    case McrDynamicVarType.String:
                        r.ReadString();
                        break;
                    case McrDynamicVarType.BaseDynamic:
                    case McrDynamicVarType.Decimal:
                        r.ReadFloat();
                        break;
                    case McrDynamicVarType.Bool:
                        r.ReadBool();
                        break;
                    default:
                        throw new McrFormatException($"invalid SerializableDynamicVar type {type}");
                }
            }
        }
        return 0;
    }

    private static int ReadAncientChoice(McrBitReader r)
    {
        r.ReadString();
        r.ReadString();
        r.ReadBool();
        return 0;
    }

    private static void SkipMapDrawings(McrBitReader r)
    {
        r.ReadList(rr =>
        {
            rr.ReadULong();
            rr.ReadList(line =>
            {
                line.ReadBool();
                int n = line.ReadCount(16);
                for (int i = 0; i < n; i++)
                {
                    line.ReadUInt(13);
                    line.ReadUInt(16);
                }
                return 0;
            });
            return 0;
        });
    }

    private static McrEvent ReadEvent(McrBitReader r)
    {
        var type = (McrEventType)r.ReadInt(3);
        switch (type)
        {
            case McrEventType.GameAction:
            {
                ulong player = r.ReadULong();
                int typeId = r.ReadByte();
                return new McrEvent { Type = type, PlayerId = player, Action = ReadNetAction(r, typeId) };
            }
            case McrEventType.HookAction:
            {
                ulong player = r.ReadULong();
                uint hookId = r.ReadUInt();
                var gat = (McrGameActionType)r.ReadEnum(MaxGameActionType);
                return new McrEvent { Type = type, PlayerId = player, HookId = hookId, GameActionType = gat };
            }
            case McrEventType.ResumeAction:
                return new McrEvent { Type = type, ActionId = r.ReadUInt() };
            case McrEventType.PlayerChoice:
            {
                ulong player = r.ReadULong();
                uint choiceId = r.ReadUInt();
                return new McrEvent { Type = type, PlayerId = player, ChoiceId = choiceId, Choice = ReadChoice(r) };
            }
            default:
                throw new McrFormatException($"invalid CombatReplayEventType {(int)type} at bit {r.BitPosition - 3}");
        }
    }

    private static McrNetAction ReadNetAction(McrBitReader r, int typeId)
    {
        if (typeId < 0 || typeId >= NetActionTypesByName.Length)
        {
            throw new McrFormatException($"net action type id {typeId} does not map to any type");
        }
        switch (NetActionTypesByName[typeId])
        {
            case "NetConsoleCmdGameAction":
                return new McrConsoleCmd(r.ReadString(), r.ReadBool());
            case "NetDiscardPotionGameAction":
                return new McrDiscardPotion(r.ReadUInt(4), r.ReadBool());
            case "NetEndPlayerTurnAction":
                return new McrEndPlayerTurn(r.ReadInt(16));
            case "NetMoveToMapCoordAction":
                return new McrMoveToMapCoord(ReadCoord(r));
            case "NetPickRelicAction":
                return new McrPickRelic(r.ReadBool() ? r.ReadInt(8) : null);
            case "NetPlayCardAction":
            {
                uint card = r.ReadUInt(16);
                string? model = r.ReadModelEntry();
                uint? target = r.ReadBool() ? r.ReadUInt(6) : null;
                return new McrPlayCard(card, model, target);
            }
            case "NetReadyToBeginEnemyTurnAction":
                return new McrReadyToBeginEnemyTurn();
            case "NetUndoEndPlayerTurnAction":
                return new McrUndoEndPlayerTurn(r.ReadInt(16));
            case "NetUsePotionAction":
            {
                uint index = r.ReadUInt(4);
                bool inCombat = r.ReadBool();
                uint? target = r.ReadBool() ? r.ReadUInt(6) : null;
                ulong? targetPlayer = r.ReadBool() ? r.ReadULong() : null;
                return new McrUsePotion(index, inCombat, target, targetPlayer);
            }
            case "NetVoteForMapCoordAction":
            {
                int act = r.ReadInt(4);
                MapCoord? source = r.ReadBool() ? ReadCoord(r) : null;
                int? gen = null;
                MapCoord? dest = null;
                if (r.ReadBool())
                {
                    gen = r.ReadInt(4);
                    dest = ReadCoord(r);
                }
                return new McrVoteForMapCoord(act, source, gen, dest);
            }
            default:
                return new McrVoteToMoveToNextAct(r.Legacy ? -1 : r.ReadInt());
        }
    }

    private static McrChoiceResult ReadChoice(McrBitReader r)
    {
        var type = (McrPlayerChoiceType)r.ReadEnum(MaxPlayerChoiceType);
        switch (type)
        {
            case McrPlayerChoiceType.CanonicalCard:
            {
                int n = r.ReadCount();
                var cards = new List<string?>(n);
                for (int i = 0; i < n; i++)
                {
                    cards.Add(r.ReadModelEntry());
                }
                return new McrChoiceResult { Type = type, CanonicalCards = cards };
            }
            case McrPlayerChoiceType.CombatCard:
                return new McrChoiceResult { Type = type, CombatCards = r.ReadList(rr => rr.ReadUInt(16)) };
            case McrPlayerChoiceType.DeckCard:
                return new McrChoiceResult { Type = type, DeckCards = r.ReadList(rr => rr.ReadUInt(16)) };
            case McrPlayerChoiceType.MutableCard:
            {
                var cards = r.ReadList(ReadCard);
                ulong? owner = r.ReadBool() ? r.ReadULong() : null;
                return new McrChoiceResult { Type = type, MutableCards = cards, MutableCardOwner = owner };
            }
            case McrPlayerChoiceType.Player:
                return new McrChoiceResult { Type = type, PlayerId = r.ReadBool() ? r.ReadULong() : null };
            case McrPlayerChoiceType.Index:
            {
                int n = r.ReadCount();
                var idx = new List<int>(n);
                for (int i = 0; i < n; i++)
                {
                    idx.Add(r.ReadInt());
                }
                return new McrChoiceResult { Type = type, Indexes = idx };
            }
            default:
                throw new McrFormatException($"invalid PlayerChoiceType {(int)type}");
        }
    }

    private static McrChecksum ReadChecksum(McrBitReader r)
    {
        uint id = r.ReadUInt();
        uint checksum = r.ReadUInt();
        string context = r.ReadString();
        var state = ReadCombatState(r);
        return new McrChecksum { Id = id, Checksum = checksum, Context = context, State = state };
    }

    public static McrCombatState ReadCombatState(McrBitReader r)
    {
        var creatures = r.ReadList(ReadCreature);
        var players = r.ReadList(ReadPlayerState);
        var rng = ReadRunRng(r);
        int nChoice = r.ReadCount();
        var choices = new List<uint>(nChoice);
        for (int i = 0; i < nChoice; i++)
        {
            choices.Add(r.ReadUInt());
        }
        int nReward = r.ReadCount();
        var rewards = new List<int>(nReward);
        for (int i = 0; i < nReward; i++)
        {
            rewards.Add(r.ReadInt());
        }
        uint? lastAction = r.ReadBool() ? r.ReadUInt() : null;
        uint? lastHook = r.ReadBool() ? r.ReadUInt() : null;
        return new McrCombatState
        {
            Creatures = creatures,
            Players = players,
            Rng = rng,
            NextChoiceIds = choices,
            NextRewardIds = rewards,
            LastExecutedActionId = lastAction,
            LastExecutedHookId = lastHook
        };
    }

    private static McrCreature ReadCreature(McrBitReader r)
    {
        string? monster = r.ReadBool() ? r.ReadModelEntry() : null;
        ulong? player = r.ReadBool() ? r.ReadULong() : null;
        int hp = r.ReadInt();
        int maxHp = r.ReadInt();
        int block = r.ReadInt();
        var powers = r.ReadList(rr =>
        {
            string? id = rr.ReadModelEntry();
            return new McrPower(id, rr.ReadInt());
        });
        return new McrCreature { MonsterId = monster, PlayerId = player, CurrentHp = hp, MaxHp = maxHp, Block = block, Powers = powers };
    }

    private static McrPlayerState ReadPlayerState(McrBitReader r)
    {
        ulong id = r.ReadULong();
        string? character = r.ReadModelEntry();
        int turn = r.ReadInt();
        var phase = (McrPlayerTurnPhase)r.ReadEnum(MaxPlayerTurnPhase);
        int energy = r.ReadInt();
        int stars = r.ReadInt();
        int maxPotions = r.ReadInt();
        int gold = r.ReadInt();
        var piles = r.ReadList(rr =>
        {
            var type = (McrPileType)rr.ReadInt();
            return new McrPile(type, rr.ReadList(ReadCardState));
        });
        var potions = r.ReadList(rr => rr.ReadModelEntry());
        var relics = r.ReadList(ReadRelic);
        var orbs = r.ReadList(rr =>
        {
            string? oid = rr.ReadModelEntry();
            int passive = rr.ReadInt(16);
            int evoke = rr.ReadInt(16);
            return new McrOrb(oid, passive, evoke);
        });
        var rng = ReadPlayerRng(r);
        var bag = ReadGrabBag(r);
        return new McrPlayerState
        {
            PlayerId = id,
            CharacterId = character,
            TurnNumber = turn,
            Phase = phase,
            Energy = energy,
            Stars = stars,
            MaxPotionCount = maxPotions,
            Gold = gold,
            Piles = piles,
            Potions = potions,
            Relics = relics,
            Orbs = orbs,
            Rng = rng,
            RelicGrabBag = bag
        };
    }

    private static McrCardState ReadCardState(McrBitReader r)
    {
        var card = ReadCard(r);
        string? affliction = null;
        int afflictionCount = 0;
        if (r.ReadBool())
        {
            affliction = r.ReadModelEntry();
            afflictionCount = r.ReadInt();
        }
        int? cost = r.ReadBool() ? r.ReadInt() : null;
        List<McrCardKeyword>? keywords = null;
        if (r.ReadBool())
        {
            int n = r.ReadInt(3);
            keywords = new List<McrCardKeyword>(n);
            for (int i = 0; i < n; i++)
            {
                keywords.Add((McrCardKeyword)r.ReadEnum(MaxCardKeyword));
            }
        }
        return new McrCardState { Card = card, Affliction = affliction, AfflictionCount = afflictionCount, EnergyCost = cost, Keywords = keywords };
    }
}
