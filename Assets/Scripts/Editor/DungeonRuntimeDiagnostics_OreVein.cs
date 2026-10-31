using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using ARPG.Component;
using ARPG.Data;
using ARPG.Dungeon;
using ARPG.Map;
using ARPG.OreVein;
using ARPG.Skill.Combat;
using ARPG.Tables;
using ARPG.Village;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace ARPG.EditorTools
{
    /// <summary>
    /// 월드 광맥 진단 (WORLD_ORE_VEIN_PLAN §9 1~4단계). 격리된 AR 복제본의 소유자(OreVeinManager·마을·던전 입구·건물 점유·가방)와
    /// 실제 manifest writer/reader로 굴림·광종 분포·위치 조건·고갈·재생·정리·저장 범위, 그리고 실제 System_PlayerMining의
    /// 채광 진행·취소·완료를 검사한다. 광맥 엔티티 프리팹은 만들지 않고 OreVeinComponent만 붙인다.
    /// 플레이어 세이브와 다른 Unity 프로젝트에는 쓰지 않는다. Temp 아래 자기 폴더만 쓰고 지운다.
    /// </summary>
    public static partial class DungeonRuntimeDiagnostics
    {
        private const int OreSeed = 12345;
        // 실제 청크 크기는 MapManager 프리팹 값(코드 기본값 8과 다를 수 있다). 진단 시작 때마다 다시 읽는다
        private static int _oreChunkSize;
        private static int OreChunkSize => _oreChunkSize;
        // 청크 탐색 범위 = 프리팹 맵 경계 안쪽 (PlaceObject·입구 지형 검사가 경계 밖을 거부한다)
        private static RectInt _oreChunkBounds;

        private enum OreCell { Any, Interior, LeftEdge }

        private static async Task ValidateOreVeins(StringBuilder report)
        {
            Require(EditorApplication.isPlaying == false, "ore vein fixture requires edit mode");
            MapManager prefabMap = Resources.Load<AR>("AR").WorldMap;
            _oreChunkSize = prefabMap.chunkSize;
            _oreChunkBounds = new RectInt(prefabMap.minChunkX + 1, 1, prefabMap.maxChunkX - prefabMap.minChunkX - 1, prefabMap.maxChunkY - 1);
            Require(_oreChunkSize >= 4 && _oreChunkBounds.width > 10 && _oreChunkBounds.height > 10, "prefab chunk size and map bounds");
            ValidateOreVeinRules(report);
            ValidateOreTypes(report);
            ValidateOreItemsAndArt(report);
            using var fixture = new OreVeinDiagnosticFixture();
            try
            {
                ValidateOreVeinRolls(fixture, report);
                ValidateOreVeinPlacement(fixture, report);
                ValidateOreVeinOverlaps(fixture, report);
                ValidateOreVeinCleanup(fixture, report);
                await ValidateOreVeinPersistence(fixture, report);
                await ValidateOreMining(fixture, report);
            }
            finally { await fixture.Data.WaitForSaveIdleAsync(); }
        }

        /// <summary>3단계: 구역 번호별 광종 비율 (§4.1). 정확한 표(굴림값 0~99)와 실제 굴림 표본을 함께 본다.</summary>
        private static void ValidateOreTypes(StringBuilder report)
        {
            int[] zones = { 1, 4, 5, 9, 10, 51 };
            int[,] expected = { { 100, 0, 0 }, { 100, 0, 0 }, { 50, 50, 0 }, { 50, 50, 0 }, { 25, 25, 50 }, { 25, 25, 50 } };
            for (int z = 0; z < zones.Length; z++)
            {
                int copper = 0, tin = 0, iron = 0;
                for (int roll = 0; roll < 100; roll++)
                {
                    OreVeinType type = OreVeinRules.SelectType(zones[z], roll);
                    if (type == OreVeinType.Copper) copper++;
                    else if (type == OreVeinType.Tin) tin++;
                    else if (type == OreVeinType.Iron) iron++;
                }
                Require(copper == expected[z, 0] && tin == expected[z, 1] && iron == expected[z, 2],
                    $"zone {zones[z]} ore share copper/tin/iron = {copper}/{tin}/{iron}");
            }
            var measured = new StringBuilder();
            int[] sampleZones = { 3, 7, 15 };
            for (int z = 0; z < sampleZones.Length; z++)
            {
                int hits = 0, copper = 0, tin = 0, iron = 0;
                for (int i = 0; i < 60000; i++)
                {
                    if (OreVeinRules.TryRoll(OreSeed, i % 300 - 150, i / 300 - 100, i % 13, OreChunkSize, sampleZones[z],
                            out _, out _, out OreVeinType type) == false)
                        continue;
                    hits++;
                    if (type == OreVeinType.Copper) copper++;
                    else if (type == OreVeinType.Tin) tin++;
                    else if (type == OreVeinType.Iron) iron++;
                }
                float c = (float)copper / hits, t = (float)tin / hits, r = (float)iron / hits;
                float ec = sampleZones[z] >= OreVeinRules.IronMinZone ? 0.25f : sampleZones[z] >= OreVeinRules.TinMinZone ? 0.5f : 1f;
                float et = sampleZones[z] >= OreVeinRules.IronMinZone ? 0.25f : sampleZones[z] >= OreVeinRules.TinMinZone ? 0.5f : 0f;
                float er = sampleZones[z] >= OreVeinRules.IronMinZone ? 0.5f : 0f;
                Require(Mathf.Abs(c - ec) < 0.03f && Mathf.Abs(t - et) < 0.03f && Mathf.Abs(r - er) < 0.03f,
                    $"sampled zone {sampleZones[z]} share {c:P1}/{t:P1}/{r:P1}");
                measured.Append($" zone {sampleZones[z]}: {c:P1}/{t:P1}/{r:P1} of {hits};");
            }
            Require(OreVeinRules.GetItemId(OreVeinType.Tin) == 2004 && OreVeinRules.GetItemId(OreVeinType.Iron) == 2003,
                "tin and iron veins map to items 2004 and 2003");
            report.AppendLine($"PASS: ore types — zone 1~4 copper only, 5~9 copper 50/tin 50, 10+ copper 25/tin 25/iron 50; sampled copper/tin/iron{measured}");
        }

        /// <summary>3단계: 광석 아이템 행(Ore 분류)과 아이템 아이콘, 광맥 그림 Addressables 주소.</summary>
        private static void ValidateOreItemsAndArt(StringBuilder report)
        {
            var items = JsonConvert.DeserializeObject<List<ItemTable>>(File.ReadAllText("Assets/_BinaryData/TableData/ItemTable.bytes"));
            var atlas = AssetDatabase.LoadAssetAtPath<UnityEngine.U2D.SpriteAtlas>("Assets/Art/Sprites/Item.spriteatlasv2");
            var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
            OreVeinType[] types = { OreVeinType.Copper, OreVeinType.Tin, OreVeinType.Iron };
            GlobalEnum.ItemType[] itemTypes = { GlobalEnum.ItemType.Copper, GlobalEnum.ItemType.Tin, GlobalEnum.ItemType.Iron };
            for (int i = 0; i < types.Length; i++)
            {
                ItemTable row = items.Find(item => item.Id == OreVeinRules.GetItemId(types[i]));
                Require(row != null && row.ItemType == itemTypes[i] && row.Category == GlobalEnum.ItemCategory.Ore && row.Stackable,
                    $"{types[i]} ore item row (Ore category, stackable)");
                Sprite icon = atlas.GetSprite(row.SpriteName);
                Require(icon != null, $"{types[i]} item icon '{row.SpriteName}' is in the item atlas");
                UnityEngine.Object.DestroyImmediate(icon);
                string path = $"Assets/Art/Sprites/Map/{types[i]}Ore.png";
                var entry = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(path));
                Require(entry != null && entry.address == $"Sprites/Map/{types[i]}Ore" && AssetDatabase.LoadAssetAtPath<Sprite>(path) != null,
                    $"{types[i]} vein sprite addressable");
            }
            report.AppendLine("PASS: ore items — copper 2002 / tin 2004 / iron 2003 rows (Ore, stackable) with item atlas icons; vein sprites addressable as Sprites/Map/*Ore.");
        }

        private static void ValidateOreVeinRules(StringBuilder report)
        {
            const int samples = 20000;
            int hits = 0;
            int seedDifferences = 0;
            for (int i = 0; i < samples; i++)
            {
                int chunkX = i % 200 - 100, chunkY = i / 200 - 50, day = i / 3 % 11;
                bool a = OreVeinRules.TryRoll(OreSeed, chunkX, chunkY, day, OreChunkSize, 1, out int ax, out int ay, out OreVeinType at);
                bool b = OreVeinRules.TryRoll(OreSeed, chunkX, chunkY, day, OreChunkSize, 1, out int bx, out int by, out OreVeinType bt);
                Require(a == b && ax == bx && ay == by && at == bt, "same seed, chunk and day roll identically");
                if (OreVeinRules.TryRoll(54321, chunkX, chunkY, day, OreChunkSize, 1, out _, out _, out _) != a) seedDifferences++;
                if (a == false) continue;
                hits++;
                Require(ax >= 0 && ax < OreChunkSize && ay >= 0 && ay < OreChunkSize && at == OreVeinType.Copper,
                    "rolled cell lies inside the chunk and zone 1 rolls copper only");
            }
            float rate = (float)hits / samples;
            Require(rate >= 0.11f && rate <= 0.13f, $"chunk roll rate near 12% (measured {rate:P2})");
            Require(seedDifferences > 0, "world seed changes the rolls");
            Require(OreVeinRules.GetDay(0f) == 0 && OreVeinRules.GetDay(23.999f) == 0 && OreVeinRules.GetDay(24f) == 1 &&
                OreVeinRules.GetDay(50.5f) == 2 && OreVeinRules.GetDay(-5f) == 0, "game day = floor(game hours / 24)");
            Require(OreVeinRules.IsTerrainClear(0UL) && OreVeinRules.IsTerrainClear((ulong)GlobalEnum.TileFlag.GroundLayerMask) &&
                OreVeinRules.IsTerrainClear((ulong)GlobalEnum.TileFlag.MonsterSpawn) &&
                OreVeinRules.IsTerrainClear((ulong)GlobalEnum.TileFlag.Hill) == false &&
                OreVeinRules.IsTerrainClear((ulong)GlobalEnum.TileFlag.Blocked) == false &&
                OreVeinRules.IsTerrainClear(7UL << 10) == false, "terrain test rejects hill, blocked and object bits only");
            var active = new OreVeinRecord { State = OreVeinState.Active, LastActiveGameTime = 100f };
            var exhausted = new OreVeinRecord { State = OreVeinState.Exhausted, RegrowGameTime = 150f };
            Require(OreVeinRules.IsCleanupDue(active, 171.9f) == false && OreVeinRules.IsCleanupDue(active, 172f) &&
                OreVeinRules.IsCleanupDue(exhausted, 149.9f) == false && OreVeinRules.IsCleanupDue(exhausted, 150f),
                "cleanup: 72 game hours after last activity, exhausted only after its regrow time");
            Require(OreVeinRules.RegrowHours >= OreVeinRules.HoursPerDay, "regrow time is never shorter than one game day");
            Require(OreVeinRules.GetItemId(OreVeinType.Copper) == 2002, "copper vein maps to item 2002");
            report.AppendLine($"PASS: ore vein rules — deterministic chunk roll ({rate:P2} of {samples} chunk-days, chunk {OreChunkSize}x{OreChunkSize}), in-chunk cell, zone 1 copper, day boundary, terrain bits, cleanup/regrow boundaries.");
        }

        private static void ValidateOreVeinRolls(OreVeinDiagnosticFixture fixture, StringBuilder report)
        {
            OreVeinManager veins = fixture.Veins;
            fixture.ResetLedger();
            // 같은 날 같은 결과
            Vector2Int hit = FindOreChunk(2, true, -1, false, OreCell.Any, 0);
            OreVeinRules.TryRoll(OreSeed, hit.x, hit.y, 2, OreChunkSize, 1, out int hx, out int hy, out _);
            MapChunkData hitChunk = fixture.Chunk(hit);
            OreVeinRecord first = veins.ResolveChunk(hit, hitChunk, 50.5f);
            Require(first != null && first.TileX == hit.x * OreChunkSize + hx && first.TileY == hit.y * OreChunkSize + hy &&
                first.Remaining == OreVeinRules.Charges && first.RollDay == 2 && first.State == OreVeinState.Active &&
                first.Type == OreVeinType.Copper && first.LastActiveGameTime == 50.5f && fixture.World.OreVeins.Count == 1,
                "a rolled chunk places one copper vein with 3 charges at the rolled cell");
            Require(veins.IsVeinTile(first.TileX, first.TileY) && veins.IsVeinTile(first.TileX + 1, first.TileY) == false, "vein tile query");
            fixture.SetGameTime(51f);
            veins.OnChunkDeactivated(hit);
            Require(first.LastActiveGameTime == 51f && veins.GetRecord(hit) == first, "deactivation refreshes the last active time and keeps the record");
            OreVeinRecord again = veins.ResolveChunk(hit, hitChunk, 60f);
            Require(ReferenceEquals(again, first) && first.LastActiveGameTime == 60f && fixture.World.OreVeins.Count == 1,
                "same-day reactivation restores the vein without re-rolling or duplicating");
            Vector2Int empty = FindOreChunk(2, false, -1, false, OreCell.Any, 0);
            Require(veins.ResolveChunk(empty, fixture.Chunk(empty), 50.5f) == null && veins.ResolveChunk(empty, fixture.Chunk(empty), 71.9f) == null &&
                veins.GetRecord(empty) == null && fixture.World.OreVeins.Count == 1, "an empty chunk stays empty all day and stores nothing");
            // 다음 날 빈 청크는 새로 굴린다
            Vector2Int next = FindOreChunk(2, false, 3, true, OreCell.Any, 0);
            Require(veins.ResolveChunk(next, fixture.Chunk(next), 70f) == null, "empty chunk on day 2");
            OreVeinRecord tomorrow = veins.ResolveChunk(next, fixture.Chunk(next), 72.5f);
            Require(tomorrow != null && tomorrow.RollDay == 3, "the next day rolls the empty chunk again");
            // 고갈 → 재생 전 없음 → 재생 뒤 다시 굴림
            Require(veins.TryConsume(hit, 1, 61f) == false, "consumption targets the chunk's roll day");
            Require(veins.TryConsume(hit, 2, 61f) && veins.TryConsume(hit, 2, 61f) && first.Remaining == 1 &&
                first.State == OreVeinState.Active, "two charges consumed");
            Require(veins.TryConsume(hit, 2, 61f) && first.State == OreVeinState.Exhausted && first.Remaining == 0 &&
                Math.Abs(first.RegrowGameTime - 85f) < 0.001f && veins.IsVeinTile(first.TileX, first.TileY) == false,
                "the last charge exhausts the vein with a 24-hour regrow time");
            Require(veins.TryConsume(hit, 2, 62f) == false, "an exhausted vein cannot be consumed again");
            Require(veins.ResolveChunk(hit, hitChunk, 84.9f) == null && veins.GetRecord(hit) == first, "no vein before the regrow time");
            bool rollsDay3 = OreVeinRules.TryRoll(OreSeed, hit.x, hit.y, 3, OreChunkSize, 1, out _, out _, out _);
            OreVeinRecord regrown = veins.ResolveChunk(hit, hitChunk, 85f);
            Require(ReferenceEquals(regrown, first) == false && (regrown != null) == rollsDay3 &&
                (regrown == null ? veins.GetRecord(hit) == null : regrown.RollDay == 3 && regrown.Remaining == OreVeinRules.Charges),
                "after the regrow time the record is cleared and the chunk rolls with the new day");
            Require(veins.TryConsume(hit, 2, 86f) == false, "the old roll day no longer targets the chunk");
            report.AppendLine("PASS: ore vein rolls — same-day restore without duplicates, empty chunks store nothing, next-day roll, roll-day targeting, exhaustion, no vein before regrow, re-roll after regrow.");
        }

        private static void ValidateOreVeinPlacement(OreVeinDiagnosticFixture fixture, StringBuilder report)
        {
            const float now = 100f;   // day 4
            Vector2Int p = FindOreChunk(4, true, -1, false, OreCell.Interior, 0);
            OreVeinRules.TryRoll(OreSeed, p.x, p.y, 4, OreChunkSize, 1, out int lx, out int ly, out _);
            int wx = p.x * OreChunkSize + lx, wy = p.y * OreChunkSize + ly;
            bool Places(MapChunkData chunk)
            {
                fixture.ResetLedger();
                return fixture.Veins.ResolveChunk(p, chunk, now) != null;
            }
            MapChunkData clear = fixture.Chunk(p);
            Require(Places(clear), "clear terrain accepts the rolled cell");
            ulong[] obstacles = { (ulong)GlobalEnum.TileFlag.Hill, (ulong)GlobalEnum.TileFlag.Blocked, 7UL << 10 };
            for (int i = 0; i < obstacles.Length; i++)
            {
                MapChunkData blocked = fixture.Chunk(p);
                blocked.tiles[lx, ly] = obstacles[i];
                Require(Places(blocked) == false, "hill, blocked and object cells are rejected: " + i);
            }
            fixture.OccupiedTiles.Add(new Vector2Int(wx, wy));
            Require(Places(clear) == false, "a building-occupied cell is rejected");
            fixture.OccupiedTiles.Clear();

            // 마을: 여유 칸 없이 마을 안만 금지 (2026-10-31 개정)
            fixture.AddVillage(new VillageData(901, new Vector2(wx, wy)) { BoundsX = wx - 2, BoundsY = wy - 2, BoundsW = 5, BoundsH = 5 });
            Require(Places(clear) == false, "a cell inside village bounds is rejected");
            fixture.ClearVillages();
            fixture.AddVillage(new VillageData(902, new Vector2(wx - 3, wy)) { BoundsX = wx - 5, BoundsY = wy - 2, BoundsW = 5, BoundsH = 5 });
            fixture.AddVillage(new VillageData(903, new Vector2(wx + 3, wy)) { BoundsX = wx + 1, BoundsY = wy - 2, BoundsW = 5, BoundsH = 5 });
            Require(Places(clear), "cells right next to village bounds are allowed (no margin)");
            fixture.ClearVillages();
            fixture.SetBlueprint(new MapFileData(MapType.Village, 6, 6, new Vector2Int(wx - 3, wy - 3)));
            Require(Places(clear) == false, "a cell inside a settlement blueprint is rejected");
            fixture.SetBlueprint(new MapFileData(MapType.Village, 6, 6, new Vector2Int(wx - 6, wy - 3)));
            Require(Places(clear), "a cell right next to a blueprint is allowed (no margin)");
            fixture.SetBlueprint(null);

            // 던전 입구 8칸 (칸 중심 직선거리)
            fixture.SetEntrance(wx + 7, wy);
            Require(Places(clear) == false, "an entrance 7 tiles away rejects the cell");
            fixture.SetEntrance(wx + 5, wy + 5);
            Require(Places(clear) == false, "an entrance 7.07 tiles away (diagonal) rejects the cell");
            fixture.SetEntrance(wx + 8, wy);
            Require(Places(clear), "an entrance exactly 8 tiles away is allowed");
            fixture.ClearEntrances();

            // 서 있을 인접 칸은 같은 청크 안에서만 찾는다
            MapChunkData enclosed = fixture.Chunk(p);
            enclosed.tiles[lx + 1, ly] = (ulong)GlobalEnum.TileFlag.Blocked;
            enclosed.tiles[lx - 1, ly] = (ulong)GlobalEnum.TileFlag.Hill;
            enclosed.tiles[lx, ly + 1] = 7UL << 10;
            enclosed.tiles[lx, ly - 1] = (ulong)GlobalEnum.TileFlag.Blocked;
            Require(Places(enclosed) == false, "a cell without a standable neighbour is rejected");
            enclosed.tiles[lx, ly - 1] = 0UL;
            fixture.OccupiedTiles.Add(new Vector2Int(wx, wy - 1));
            Require(Places(enclosed) == false, "a building-occupied neighbour is not standable");
            fixture.OccupiedTiles.Clear();
            Require(Places(enclosed), "one free in-chunk neighbour is enough");
            Vector2Int edge = FindOreChunk(4, true, -1, false, OreCell.LeftEdge, 0);
            OreVeinRules.TryRoll(OreSeed, edge.x, edge.y, 4, OreChunkSize, 1, out int ex, out int ey, out _);
            MapChunkData edgeChunk = fixture.Chunk(edge);
            edgeChunk.tiles[ex + 1, ey] = (ulong)GlobalEnum.TileFlag.Blocked;
            edgeChunk.tiles[ex, ey + 1] = (ulong)GlobalEnum.TileFlag.Blocked;
            edgeChunk.tiles[ex, ey - 1] = (ulong)GlobalEnum.TileFlag.Blocked;
            fixture.ResetLedger();
            Require(fixture.Veins.ResolveChunk(edge, edgeChunk, now) == null, "a neighbour in the next chunk is not consulted");
            report.AppendLine("PASS: ore vein placement — hill/blocked/object/building cells, inside village bounds and blueprints only (no margin), entrance clearance 8, in-chunk standable neighbour.");
        }

        private static void ValidateOreVeinOverlaps(OreVeinDiagnosticFixture fixture, StringBuilder report)
        {
            OreVeinManager veins = fixture.Veins;
            const float now = 100f;
            // 마을 건설 우선: PlaceObject로 광맥 칸을 덮으면 기록이 지워진다
            fixture.ResetLedger();
            Vector2Int p = FindOreChunk(4, true, -1, false, OreCell.Interior, 1);
            MapChunkData chunk = fixture.Chunk(p);
            OreVeinRecord record = veins.ResolveChunk(p, chunk, now);
            Require(record != null, "fixture vein placed");
            Require(fixture.Ar.WorldMap.PlaceObject(record.TileX + 1, record.TileY, 7) && veins.GetRecord(p) == record,
                "village placement next to the vein keeps it");
            Require(fixture.Ar.WorldMap.PlaceObject(record.TileX, record.TileY, 7) && veins.GetRecord(p) == null &&
                fixture.World.OreVeins.Count == 0 && veins.IsVeinTile(record.TileX, record.TileY) == false,
                "village placement on the vein tile wins and deletes the record");
            chunk.tiles[record.TileX - p.x * OreChunkSize, record.TileY - p.y * OreChunkSize] = (7UL << 10) | (ulong)GlobalEnum.TileFlag.Blocked;
            Require(veins.ResolveChunk(p, chunk, now + 1f) == null && veins.GetRecord(p) == null,
                "the same-day re-roll lands on the building and the chunk stays empty");
            // 건물(W×H) 범위
            fixture.ResetLedger();
            Vector2Int q = FindOreChunk(4, true, -1, false, OreCell.Any, 2);
            OreVeinRecord building = veins.ResolveChunk(q, fixture.Chunk(q), now);
            Require(building != null, "second fixture vein placed");
            veins.RemoveVeinsInFootprint(building.TileX + 1, building.TileY - 1, 3, 3);
            Require(veins.GetRecord(q) == building, "a footprint beside the vein keeps it");
            veins.RemoveVeinsInFootprint(building.TileX - 1, building.TileY - 1, 3, 3);
            Require(veins.GetRecord(q) == null, "a multi-tile footprint covering the vein deletes it");

            // 복원 때 장애물과 겹친 기록(구 세이브)은 그리지 않고 지운다
            fixture.ResetLedger();
            Vector2Int r = FindOreChunk(4, true, -1, false, OreCell.Any, 3);
            MapChunkData restoreChunk = fixture.Chunk(r);
            OreVeinRecord restored = veins.ResolveChunk(r, restoreChunk, now);
            Require(restored != null, "third fixture vein placed");
            fixture.OccupiedTiles.Add(new Vector2Int(restored.TileX, restored.TileY));
            Require(veins.ResolveChunk(r, restoreChunk, now + 1f) == null && veins.GetRecord(r) == null,
                "a restored record under a building is deleted and the same-day re-roll stays empty");
            fixture.OccupiedTiles.Clear();

            // 던전 입구 배치(ValidateEntrance)는 광맥 칸을 피한다
            fixture.ResetLedger();
            Vector2Int tile = fixture.FindClearEntranceTile();
            var owner = new VillageData(904, new Vector2(tile.x + 30, tile.y))
                { BoundsX = tile.x + 28, BoundsY = tile.y - 2, BoundsW = 4, BoundsH = 4 };
            fixture.AddVillage(owner);
            fixture.VillageSlots[owner.VillageId] = new VillageDungeonSlots { VillageId = owner.VillageId };
            var slot = new DungeonSlot();
            Require(fixture.ValidateEntrance(owner, slot, tile.x, tile.y), "entrance fixture tile is valid without a vein");
            fixture.InsertVein(tile.x + 1, tile.y + 1, now);
            Require(fixture.ValidateEntrance(owner, slot, tile.x, tile.y) == false, "entrance placement rejects a vein in its 3x3 footprint");
            veins.RemoveVeinsInFootprint(tile.x + 1, tile.y + 1, 1, 1);
            Require(fixture.ValidateEntrance(owner, slot, tile.x, tile.y), "entrance placement accepts the tile once the vein is gone");
            fixture.ClearVillages();
            fixture.VillageSlots.Clear();
            report.AppendLine("PASS: ore vein overlaps — village tile/building placement deletes the vein, restore under an obstacle deletes and re-rolls empty, entrance placement avoids vein tiles.");
        }

        private static void ValidateOreVeinCleanup(OreVeinDiagnosticFixture fixture, StringBuilder report)
        {
            OreVeinManager veins = fixture.Veins;
            fixture.ResetLedger();
            OreVeinRecord stale = fixture.InsertVein(-60 * OreChunkSize + 3, 10 * OreChunkSize + 3, 100f);
            OreVeinRecord exhausted = fixture.InsertVein(-61 * OreChunkSize + 3, 10 * OreChunkSize + 3, 100f);
            exhausted.State = OreVeinState.Exhausted; exhausted.Remaining = 0; exhausted.RegrowGameTime = 180f;
            OreVeinRecord visible = fixture.InsertVein(-62 * OreChunkSize + 3, 10 * OreChunkSize + 3, 100f);
            var visibleChunk = new Vector2Int(-62, 10);
            fixture.RenderedChunks.Add(visibleChunk);
            veins.TickCleanup(160f);   // day 6
            Require(fixture.World.OreVeins.Count == 3, "nothing is due on day 6");
            veins.TickCleanup(175f);   // day 7
            Require(veins.GetRecord(new Vector2Int(-60, 10)) == null && veins.GetRecord(new Vector2Int(-61, 10)) == exhausted &&
                veins.GetRecord(visibleChunk) == visible, "an active record 72 hours stale is deleted; exhausted waits for regrow; active chunks are skipped");
            veins.TickCleanup(185f);   // still day 7
            Require(veins.GetRecord(new Vector2Int(-61, 10)) == exhausted, "cleanup runs once per game day");
            veins.TickCleanup(192.5f); // day 8
            Require(veins.GetRecord(new Vector2Int(-61, 10)) == null && veins.GetRecord(visibleChunk) == visible,
                "an exhausted record is deleted only after its regrow time");
            fixture.RenderedChunks.Remove(visibleChunk);
            veins.TickCleanup(216.5f); // day 9
            Require(veins.GetRecord(visibleChunk) == null && fixture.World.OreVeins.Count == 0 && fixture.World.OreVeins.Contains(stale) == false,
                "a stale record is deleted once its chunk is off");

            // 로드 보정: 빈 칸·잘못된 값·청크 밖 칸·같은 청크 중복은 버린다
            fixture.World.OreVeins.Clear();
            OreVeinRecord valid = Vein(5, 5, 5 * OreChunkSize + 1, 5 * OreChunkSize + 1);
            fixture.World.OreVeins.Add(valid);
            fixture.World.OreVeins.Add(null);
            fixture.World.OreVeins.Add(Vein(5, 5, 5 * OreChunkSize + 2, 5 * OreChunkSize + 2));
            fixture.World.OreVeins.Add(Vein(6, 5, 5 * OreChunkSize + 2, 5 * OreChunkSize + 2));
            OreVeinRecord noCharges = Vein(7, 5, 7 * OreChunkSize, 5 * OreChunkSize); noCharges.Remaining = 0;
            fixture.World.OreVeins.Add(noCharges);
            OreVeinRecord noType = Vein(8, 5, 8 * OreChunkSize, 5 * OreChunkSize); noType.Type = OreVeinType.None;
            fixture.World.OreVeins.Add(noType);
            veins.Initialize();
            Require(fixture.World.OreVeins.Count == 1 && fixture.World.OreVeins[0] == valid && veins.RecordCount == 1,
                "load keeps one valid record per chunk and drops null, duplicate, out-of-chunk and invalid records");
            report.AppendLine("PASS: ore vein cleanup — 72-hour stale active records, exhausted after regrow only, active chunks skipped, once per game day, load sanitizing.");
        }

        private static async Task ValidateOreVeinPersistence(OreVeinDiagnosticFixture fixture, StringBuilder report)
        {
            OreVeinManager veins = fixture.Veins;
            DataManager data = fixture.Data;
            fixture.ResetLedger();
            Vector2Int p = FindOreChunk(4, true, -1, false, OreCell.Any, 4);
            OreVeinRecord record = veins.ResolveChunk(p, fixture.Chunk(p), 100f);
            Require(record != null, "persistence fixture vein placed");
            string slot = fixture.SlotPath;
            Require(await Write(data, SaveScope.All), "full save");
            SaveManifest full = ReadManifest(slot);
            Require(full.Parts.ContainsKey("ore_veins") && full.Parts.Count == 45, "a full save writes the ore_veins partition");
            OreVeinSaveRoot loaded = LoadedVeins(data, slot);
            Require(loaded.Veins.Count == 1 && JsonConvert.SerializeObject(loaded.Veins[0], JsonSettings.Default) ==
                JsonConvert.SerializeObject(record, JsonSettings.Default), "ore vein record round trip");

            // 던전 입장 범위 저장도 광맥 잔량을 함께 확정한다 (광석 복사 없음)
            Require(veins.TryConsume(p, 4, 101f), "consume one charge");
            Require(await Write(data, SaveScope.Player | SaveScope.World | SaveScope.Map | SaveScope.NpcState | SaveScope.Dungeons), "dungeon entry scope");
            SaveManifest entry = ReadManifest(slot);
            Require(entry.Parts["ore_veins"].Revision == entry.Revision && LoadedVeins(data, slot).Veins[0].Remaining == 2,
                "a dungeon entry save commits the charge in the same revision");
            // 아이템 줍기 범위 (Player|World|Map)
            Require(veins.TryConsume(p, 4, 102f), "consume second charge");
            Require(await Write(data, SaveScope.Player | SaveScope.World | SaveScope.Map), "item pickup scope");
            Require(LoadedVeins(data, slot).Veins[0].Remaining == 1, "an item pickup save commits the charge");
            // Player가 없는 저장(입구 배치)은 광맥 파트를 싣지 않는다
            Require(veins.TryConsume(p, 4, 103f) && record.State == OreVeinState.Exhausted, "exhaust");
            long beforeRevision = ReadManifest(slot).Parts["ore_veins"].Revision;
            Require(await Write(data, SaveScope.Dungeons | SaveScope.World), "entrance placement scope");
            Require(ReadManifest(slot).Parts["ore_veins"].Revision == beforeRevision && LoadedVeins(data, slot).Veins[0].Remaining == 1,
                "saves without Player leave the ore_veins partition untouched");
            Require(await Write(data, SaveScope.Player | SaveScope.World), "player scope");
            OreVeinSaveRoot exhaustedRoot = LoadedVeins(data, slot);
            Require(exhaustedRoot.Veins[0].State == OreVeinState.Exhausted && Math.Abs(exhaustedRoot.Veins[0].RegrowGameTime - 127f) < 0.001f,
                "exhaustion and regrow time persist");
            // 로드 뒤 유지: 읽은 기록으로 색인을 다시 만든다
            fixture.World.OreVeins.Clear();
            fixture.World.OreVeins.AddRange(exhaustedRoot.Veins);
            veins.Initialize();
            OreVeinRecord reloaded = veins.GetRecord(p);
            Require(reloaded != null && reloaded.TileX == record.TileX && reloaded.TileY == record.TileY && reloaded.RollDay == 4 &&
                reloaded.State == OreVeinState.Exhausted, "the loaded ledger restores the chunk record");

            // 구 세이브: ore_veins 파트가 없으면 빈 기록으로 연다
            SaveManifest latest = ReadManifest(slot);
            string missing = Path.Combine(fixture.RootPath, "ore-missing");
            Directory.CreateDirectory(Path.Combine(missing, "parts"));
            SaveManifest missingManifest = JsonConvert.DeserializeObject<SaveManifest>(JsonConvert.SerializeObject(latest));
            missingManifest.Parts.Remove("ore_veins");
            foreach (SavePartReference part in missingManifest.Parts.Values)
                File.Copy(Path.Combine(slot, "parts", part.FileName), Path.Combine(missing, "parts", part.FileName));
            File.WriteAllText(Path.Combine(missing, "manifest.json"), JsonConvert.SerializeObject(missingManifest));
            object missingSnapshot = ReadSnapshot(data, missing, "manifest.json");
            Require(missingSnapshot != null && ((OreVeinSaveRoot)Field(missingSnapshot, "OreVeins")).Veins.Count == 0,
                "a save without the ore_veins partition opens with an empty ledger");
            // 손상된 파트는 거절하고 이전 manifest로 되돌아간다
            string corrupt = Path.Combine(fixture.RootPath, "ore-corrupt");
            Directory.CreateDirectory(Path.Combine(corrupt, "parts"));
            foreach (SavePartReference part in latest.Parts.Values)
                File.Copy(Path.Combine(slot, "parts", part.FileName), Path.Combine(corrupt, "parts", part.FileName));
            File.WriteAllText(Path.Combine(corrupt, "manifest.json"), JsonConvert.SerializeObject(latest));
            File.AppendAllText(Path.Combine(corrupt, "parts", latest.Parts["ore_veins"].FileName), "corrupt");
            Require(ReadSnapshot(data, corrupt, "manifest.json") == null && ReadSnapshot(data, slot, "manifest.prev.json") != null,
                "a corrupt ore_veins partition is rejected; the previous revision stays readable");
            report.AppendLine("PASS: ore vein persistence — full save part, round trip, Player-scope saves (dungeon entry, item pickup) commit charges with the same revision, non-Player saves leave it, exhaustion persists, reload restores, missing part opens empty, corrupt part rejected.");
        }

        /// <summary>
        /// 3·4단계: 구역 번호로 고른 광종이 기록에 남는지, 그리고 실제 System_PlayerMining의 E 안내 후보·1.2초 완료·취소(이동·밀림·피격·
        /// 공격/스킬·거리)·가방 가득·마지막 잔량·중복 획득 방지와, 채광 저장이 광석과 잔량을 같은 manifest로 확정하는지 본다.
        /// </summary>
        private static async Task ValidateOreMining(OreVeinDiagnosticFixture fixture, StringBuilder report)
        {
            OreVeinManager veins = fixture.Veins;
            ComponentManager cm = fixture.Components;
            var mining = new ARPG.Systems.System_PlayerMining();
            const float now = 100f;
            fixture.SetGameTime(now);

            // 구역 번호로 고른 광종이 기록에 남는다
            fixture.ResetLedger();
            Vector2Int far = Vector2Int.zero;
            bool foundIron = false;
            for (int x = _oreChunkBounds.xMin; x < _oreChunkBounds.xMax && foundIron == false; x++)
                if (OreVeinRules.TryRoll(OreSeed, x, 30, 4, OreChunkSize, 12, out _, out _, out OreVeinType rolled) && rolled == OreVeinType.Iron)
                { far = new Vector2Int(x, 30); foundIron = true; }
            Require(foundIron, "fixture iron chunk");
            MapChunkData farChunk = fixture.Chunk(far);
            farChunk.zone = 12;
            OreVeinRecord iron = veins.ResolveChunk(far, farChunk, now);
            Require(iron != null && iron.Type == OreVeinType.Iron, "a zone 12 chunk stores the zone-selected ore type");
            int ironEntity = fixture.AddVeinEntity(iron, 501);
            Require(veins.GetInteractionPrompt(ironEntity) == "철 광맥 채광 (남은 3회)", "iron prompt uses the item name");

            fixture.ResetLedger();
            Vector2Int p = FindOreChunk(4, true, -1, false, OreCell.Interior, 5);
            OreVeinRecord record = veins.ResolveChunk(p, fixture.Chunk(p), now);
            Require(record != null && record.Type == OreVeinType.Copper, "zone 1 copper vein for mining");
            int vein = fixture.AddVeinEntity(record, 500);
            var center = new Vector2(record.TileX + 0.5f, record.TileY + 0.5f);
            Vector2 stand = center + new Vector2(1f, 0f);

            // E 안내 후보: 1.5칸 안
            fixture.PlacePlayer(stand, 100);
            Require(veins.TryGetInteraction(out ServiceUIRouter.Candidate candidate) && candidate.Kind == ServiceUIRouter.CandidateKind.OreVein &&
                candidate.EntityId == vein && candidate.HasTile && candidate.TileX == record.TileX, "a vein within 1.5 tiles is an E candidate");
            Require(veins.GetInteractionPrompt(vein) == "구리 광맥 채광 (남은 3회)", "copper prompt with remaining charges");
            fixture.PlacePlayer(center + new Vector2(1.6f, 0f), 100);
            Require(veins.TryGetInteraction(out _) == false, "a vein 1.6 tiles away is not a candidate");

            // 1.2초 뒤 광석 1개와 잔량 1
            fixture.PlacePlayer(stand, 100);
            int copper = fixture.CountItem(OreVeinRules.CopperItemId);
            Require(veins.TryBeginMining(1, vein) && cm.HasComponent<MiningComponent>(1), "E starts mining");
            mining.OnFixedUpdate(0.5f); mining.OnFixedUpdate(0.5f);
            Require(cm.HasComponent<MiningComponent>(1) && record.Remaining == 3 && fixture.CountItem(OreVeinRules.CopperItemId) == copper,
                "after 1.0 s nothing is granted yet");
            Require(veins.TryBeginMining(1, vein) && cm.TryGetComponent<MiningComponent>(1, out var progress) && Mathf.Abs(progress.Elapsed - 1f) < 0.001f,
                "pressing E again on the same vein keeps the progress");
            mining.OnFixedUpdate(0.25f);
            Require(cm.HasComponent<MiningComponent>(1) == false && record.Remaining == 2 && fixture.CountItem(OreVeinRules.CopperItemId) == copper + 1,
                "after 1.25 s one copper ore is granted and one charge consumed");
            await fixture.Data.WaitForSaveIdleAsync();
            SaveManifest saved = ReadManifest(fixture.SlotPath);
            object snapshot = ReadSnapshot(fixture.Data, fixture.SlotPath, "manifest.json");
            PlayerData savedPlayer = ((PlayerSaveRoot)Field(snapshot, "Player")).Players[0];
            int savedCopper = 0;
            for (int i = 0; i < savedPlayer._inventory.Count; i++)
                if (savedPlayer._inventory[i] != null && savedPlayer._inventory[i].Id == OreVeinRules.CopperItemId) savedCopper += savedPlayer._inventory[i].Quantity;
            OreVeinSaveRoot savedVeins = (OreVeinSaveRoot)Field(snapshot, "OreVeins");
            Require(saved.Parts["player"].Revision == saved.Revision && saved.Parts["ore_veins"].Revision == saved.Revision &&
                savedCopper == copper + 1 && savedVeins.Veins.Count == 1 && savedVeins.Veins[0].Remaining == 2,
                "the mining save commits the ore and the charge in the same manifest");

            // 취소: 잔량과 가방은 그대로
            void Cancelled(string label)
            {
                mining.OnFixedUpdate(0.1f);
                Require(cm.HasComponent<MiningComponent>(1) == false && record.Remaining == 2 &&
                    fixture.CountItem(OreVeinRules.CopperItemId) == copper + 1, label);
                fixture.PlacePlayer(stand, 100);
            }
            Require(veins.TryBeginMining(1, vein), "restart"); fixture.SetInput(Vector2.right, false, 0);
            Cancelled("moving cancels mining");
            Require(veins.TryBeginMining(1, vein), "restart"); fixture.MovePlayer(stand + new Vector2(0.2f, 0f));
            Cancelled("being pushed away cancels mining");
            Require(veins.TryBeginMining(1, vein), "restart");
            DamageCalculator.ApplyDamageResult(0, 2, 1, new DamageResult { FinalDamage = 10 }, false, default);
            fixture.SetHp(100);
            Cancelled("damage followed by healing in the same tick cancels mining");
            Require(veins.TryBeginMining(1, vein), "restart"); fixture.SetHp(110);
            mining.OnFixedUpdate(0.1f);
            Require(cm.HasComponent<MiningComponent>(1), "regeneration does not cancel mining");
            fixture.SetHp(90);
            mining.OnFixedUpdate(0.1f);
            Require(cm.HasComponent<MiningComponent>(1), "a non-damage HP clamp does not cancel mining");
            DamageCalculator.ApplyDamageResult(0, 2, 1, new DamageResult { FinalDamage = 10, IsEvaded = true }, false, default);
            mining.OnFixedUpdate(0.1f);
            Require(cm.HasComponent<MiningComponent>(1), "evaded damage does not cancel mining");
            fixture.SetInput(Vector2.zero, true, 0);
            Cancelled("attacking cancels mining");
            Require(veins.TryBeginMining(1, vein), "restart"); fixture.SetInput(Vector2.zero, false, 1 << 2);
            Cancelled("a skill input cancels mining");
            fixture.PlacePlayer(center + new Vector2(2f, 0f), 100);
            Require(veins.TryBeginMining(1, vein), "restart far");
            Cancelled("leaving the vein range cancels mining");

            // 가방이 가득 차면 지급도 소비도 하지 않는다
            fixture.FillInventory();
            Require(veins.TryBeginMining(1, vein), "restart with a full bag");
            mining.OnFixedUpdate(0.7f); mining.OnFixedUpdate(0.7f);
            Require(cm.HasComponent<MiningComponent>(1) == false && record.Remaining == 2 && fixture.CountItem(OreVeinRules.CopperItemId) == 0,
                "a full bag keeps the charge and grants nothing");
            fixture.ClearInventory();

            // 마지막 잔량까지 캔 뒤에는 다시 캘 수 없고 같은 대상으로 두 번 얻지 않는다
            for (int i = 0; i < 2; i++)
            {
                Require(veins.TryBeginMining(1, vein), "mine remaining charge " + i);
                mining.OnFixedUpdate(0.7f); mining.OnFixedUpdate(0.7f);
            }
            Require(record.Remaining == 0 && record.State == OreVeinState.Exhausted && fixture.CountItem(OreVeinRules.CopperItemId) == 2 &&
                veins.TryBeginMining(1, vein) == false, "the last charge exhausts the vein and it cannot be mined again");
            cm.SetComponent(1, new MiningComponent { ChunkX = p.x, ChunkY = p.y, RollDay = record.RollDay, Elapsed = 1.19f,
                StartPosition = stand });
            mining.OnFixedUpdate(0.1f);
            Require(cm.HasComponent<MiningComponent>(1) == false && fixture.CountItem(OreVeinRules.CopperItemId) == 2,
                "a stale mining job on an exhausted vein grants nothing");
            await fixture.Data.WaitForSaveIdleAsync();
            fixture.ResetLedger();
            record = veins.ResolveChunk(p, fixture.Chunk(p), now);
            fixture.AddVeinEntity(record, 500);
            fixture.PlacePlayer(stand, 100);
            cm.AddComponent(999, new DestroyTag());
            veins.OnChunkDeactivated(p);
            Require(cm.HasComponent<DestroyTag>(500) && cm.HasComponent<DestroyTag>(999) && cm.HasComponent<OreVeinComponent>(500),
                "deactivation queues ore without processing unrelated destruction");
            fixture.AddVeinEntity(record, 502);
            Require(veins.TryBeginMining(1, 500) == false && veins.TryGetInteraction(out var replacement) && replacement.EntityId == 502,
                "pending ore cannot be selected after a replacement appears");
            fixture.ResetLedger();
            Require(cm.HasComponent<OreVeinComponent>(500) == false && cm.HasComponent<DestroyTag>(500) == false &&
                cm.HasComponent<OreVeinComponent>(502) == false && cm.HasComponent<DestroyTag>(999),
                "reset removes active and pending ore without processing unrelated entities");
            cm.RemoveComponent<DestroyTag>(999);
            report.AppendLine("PASS: ore mining — completion and persistence, cancellation including damage followed by healing, evasion and HP clamp, full bag, exhaustion, deferred removal and reset of pending ore.");
        }

        private static OreVeinSaveRoot LoadedVeins(DataManager data, string slot)
        {
            object snapshot = ReadSnapshot(data, slot, "manifest.json");
            Require(snapshot != null, "manifest readable");
            return (OreVeinSaveRoot)Field(snapshot, "OreVeins");
        }

        private static OreVeinRecord Vein(int chunkX, int chunkY, int tileX, int tileY) => new()
        {
            ChunkX = chunkX, ChunkY = chunkY, TileX = tileX, TileY = tileY, Type = OreVeinType.Copper,
            Remaining = OreVeinRules.Charges, RollDay = 4, State = OreVeinState.Active, LastActiveGameTime = 100f,
        };

        /// <summary>맵 경계 안 청크를 고정 순서로 훑어 굴림 결과가 원하는 모양인 nth번째 청크를 찾는다.</summary>
        private static Vector2Int FindOreChunk(int day, bool vein, int nextDay, bool nextVein, OreCell cell, int nth)
        {
            for (int y = _oreChunkBounds.yMin; y < _oreChunkBounds.yMax; y++)
                for (int x = _oreChunkBounds.xMin; x < _oreChunkBounds.xMax; x++)
                {
                    bool rolled = OreVeinRules.TryRoll(OreSeed, x, y, day, OreChunkSize, 1, out int lx, out int ly, out _);
                    if (rolled != vein) continue;
                    if (nextDay >= 0 && OreVeinRules.TryRoll(OreSeed, x, y, nextDay, OreChunkSize, 1, out _, out _, out _) != nextVein) continue;
                    if (cell == OreCell.Interior && (lx < 1 || lx > OreChunkSize - 2 || ly < 1 || ly > OreChunkSize - 2)) continue;
                    if (cell == OreCell.LeftEdge && (lx != 0 || ly < 1 || ly > OreChunkSize - 2)) continue;
                    if (nth-- > 0) continue;
                    return new Vector2Int(x, y);
                }
            throw new InvalidOperationException("no fixture chunk for the requested ore roll pattern");
        }

        /// <summary>격리된 AR 복제본. 광맥 판단이 읽는 소유자(데이터·맵·마을·던전 입구·건물 점유·시간)만 채운다.</summary>
        private sealed class OreVeinDiagnosticFixture : IDisposable
        {
            private readonly GameObject _go;
            private readonly FieldInfo _singleton = typeof(PrefabSingleton<AR>).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
            private readonly object _previousAr;
            private readonly string _path;
            public AR Ar { get; }
            public DataManager Data => Ar.Data;
            public OreVeinManager Veins => Ar.OreVein;
            public ComponentManager Components => Ar.Component;
            public WorldData World { get; }
            public PlayerData Player { get; }
            public string RootPath => _path;
            public string SlotPath => Path.Combine(_path, "Slot_0");
            public HashSet<Vector2Int> RenderedChunks { get; }
            public HashSet<Vector2Int> OccupiedTiles { get; }
            public Dictionary<int, VillageDungeonSlots> VillageSlots { get; }
            private readonly Dictionary<Vector2Int, List<DungeonSlot>> _entrancesByChunk;
            private readonly Dictionary<int, VillageData> _villages;
            private readonly List<VillageData> _villageList;
            private readonly Dictionary<Vector2Int, MapFileData> _blueprints;
            private readonly List<MapFileData> _blueprintCache;

            public OreVeinDiagnosticFixture()
            {
                _previousAr = _singleton.GetValue(null);
                _path = Path.GetFullPath(Path.Combine("Temp", "DungeonDiagnostics", Guid.NewGuid().ToString("N")));
                _go = UnityEngine.Object.Instantiate(Resources.Load<AR>("AR").gameObject);
                _go.name = "OreVeinDiagnostic"; _go.hideFlags = HideFlags.HideAndDontSave;
                _go.SetActive(false);
                Ar = _go.GetComponent<AR>(); _singleton.SetValue(null, Ar);
                try
                {
                    InvalidateComponentCaches();
                    World = new WorldData { WorldSeed = OreSeed };
                    Set(Data, "_worldData", World);
                    Player = new PlayerData { PlayerId = 1, CurrentHp = 100 };
                    for (int i = 0; i < GlobalEnum.PLAYER_INVENTORY_SLOTCOUNT_MAX; i++) Player._inventory.Add(null);
                    Set(Data, "_playerDataList", new List<PlayerData> { Player });
                    Set(Data, "_currentPlayerEntityId", 1);
                    Set(Data, "_editorMainThreadId", Environment.CurrentManagedThreadId);
                    // 격리 DataManager는 표를 읽지 않는다. PlaceObject가 조회하는 건설 표는 빈 표로(타일 경로),
                    // 채광이 만드는 광석 아이템과 안내 문구의 광종 이름은 실제 아이템 표로 둔다.
                    Set(Data, "_buildableItemTable", ImmutableDictionary<int, BuildableItemTable>.Empty);
                    var items = JsonConvert.DeserializeObject<List<ItemTable>>(File.ReadAllText("Assets/_BinaryData/TableData/ItemTable.bytes"));
                    Set(Data, "_itemTable", ImmutableDictionary.CreateRange(items.ConvertAll(item => new KeyValuePair<int, ItemTable>(item.Id, item))));
                    // 아이템 생성이 장비 여부를 고유 옵션 표로 판별한다 — 광석은 장비가 아니므로 빈 표
                    Set(Data, "_itemImplicitTable", ImmutableDictionary<int, ItemImplicitTable>.Empty);
                    // 채광 지급은 실제 가방 경로를 쓴다 (AddItem → 가방 변경 표시)
                    Set(Ar, "_playerManager", new ARPG.Manager.PlayerManager());
                    Ar.Player.InitializePlayerData();
                    Data.DiagnosticSavePath = _path;
                    RenderedChunks = new HashSet<Vector2Int>();
                    Set(Ar.WorldMap, "_renderedChunks", RenderedChunks);
                    Set(Ar.WorldMap, "_renderedChunkCoords", new List<Vector2Int>());
                    Set(Ar.WorldMap, "_activeChunks", new Dictionary<Vector2Int, MapChunkData>());
                    _blueprints = Private<Dictionary<Vector2Int, MapFileData>>(Ar.WorldMap, "_mapFileDataDic");
                    _blueprintCache = Private<List<MapFileData>>(Ar.WorldMap, "_placementBlueprints");
                    _blueprints.Clear(); _blueprintCache.Clear();
                    OccupiedTiles = Private<HashSet<Vector2Int>>(Ar.Building, "_occupiedTiles");
                    _villages = Private<Dictionary<int, VillageData>>(Ar.Village, "_villages");
                    _villageList = Private<List<VillageData>>(Ar.Village, "_villageList");
                    _entrancesByChunk = Private<Dictionary<Vector2Int, List<DungeonSlot>>>(Ar.Dungeon, "_entrancesByChunk");
                    VillageSlots = Private<Dictionary<int, VillageDungeonSlots>>(Ar.Dungeon, "_villageSlots");
                    Require(Ar.WorldMap.chunkSize == OreChunkSize, "fixture map uses the prefab chunk size");
                    Veins.Initialize();
                }
                catch { Dispose(); throw; }
            }

            public MapChunkData Chunk(Vector2Int coord) => new(OreChunkSize) { chunkX = coord.x, chunkY = coord.y };

            /// <summary>빈 기록으로 되돌리고 매니저 런타임 값(마지막 정리 날짜 등)도 새로 시작한다.</summary>
            public void ResetLedger()
            {
                World.OreVeins.Clear();
                Veins.Reset();
                Veins.Initialize();
            }

            public void SetGameTime(float hours)
            {
                Set(Ar.Time, "_elapsedGameTime", hours);
                Set(Ar.Time, "_accumulator", 0f);
            }

            public OreVeinRecord InsertVein(int tileX, int tileY, float lastActive)
            {
                var record = Vein(Mathf.FloorToInt((float)tileX / OreChunkSize), Mathf.FloorToInt((float)tileY / OreChunkSize), tileX, tileY);
                record.LastActiveGameTime = lastActive;
                World.OreVeins.Add(record);
                Veins.Initialize();
                return record;
            }

            public void AddVillage(VillageData village) { _villages.Add(village.VillageId, village); _villageList.Add(village); }
            public void ClearVillages() { _villages.Clear(); _villageList.Clear(); }

            public void SetBlueprint(MapFileData blueprint)
            {
                _blueprints.Clear(); _blueprintCache.Clear();
                if (blueprint != null) _blueprints.Add(blueprint.StartPosition, blueprint);
            }

            public void SetEntrance(int x, int y)
            {
                ClearEntrances();
                var slot = new DungeonSlot { HasEntrance = true, EntranceX = x, EntranceY = y };
                var chunk = new Vector2Int(Mathf.FloorToInt((float)x / OreChunkSize), Mathf.FloorToInt((float)y / OreChunkSize));
                _entrancesByChunk.Add(chunk, new List<DungeonSlot> { slot });
            }

            public void ClearEntrances() => _entrancesByChunk.Clear();

            /// <summary>광맥 엔티티 대신 OreVeinComponent만 붙이고 매니저 엔티티 색인에 넣는다 (프리팹·Addressables 없이).</summary>
            public int AddVeinEntity(OreVeinRecord record, int entityId)
            {
                Components.SetComponent(entityId, new OreVeinComponent { ChunkX = record.ChunkX, ChunkY = record.ChunkY, RollDay = record.RollDay });
                Private<Dictionary<Vector2Int, int>>(Veins, "_entityByChunk")[new Vector2Int(record.ChunkX, record.ChunkY)] = entityId;
                return entityId;
            }

            /// <summary>플레이어 엔티티(1)를 그 자리에 두고 HP·입력을 새로 시작한다.</summary>
            public void PlacePlayer(Vector2 position, int hp)
            {
                MovePlayer(position);
                SetHp(hp);
                SetInput(Vector2.zero, false, 0);
            }

            public void MovePlayer(Vector2 position) => Components.SetComponent(1, new TransformComponent { Position = position, Scale = Vector2.one });

            public void SetHp(int hp)
            {
                var stat = new StatComponent();
                stat.SetCurrentHpDirect(hp);
                Components.SetComponent(1, stat);
            }

            public void SetInput(Vector2 move, bool attacking, int skillMask)
                => Components.SetComponent(1, new InputComponent { MoveDirection = move, IsAttacking = attacking, SkillSlotHeldMask = skillMask });

            public int CountItem(int itemId)
            {
                int count = 0;
                for (int i = 0; i < Player._inventory.Count; i++)
                    if (Player._inventory[i] != null && Player._inventory[i].Id == itemId) count += Player._inventory[i].Quantity;
                return count;
            }

            /// <summary>모든 칸을 광석이 아닌 겹치지 않는 아이템으로 채운다.</summary>
            public void FillInventory()
            {
                ItemTable sword = Data.GetItem(1);
                for (int i = 0; i < Player._inventory.Count; i++)
                    Player._inventory[i] = new ItemData { Id = 1, Quantity = 1, Table = sword };
            }

            public void ClearInventory()
            {
                for (int i = 0; i < Player._inventory.Count; i++) Player._inventory[i] = null;
            }

            public bool ValidateEntrance(VillageData owner, DungeonSlot slot, int x, int y)
                => (bool)typeof(DungeonManager).GetMethod("ValidateEntrance", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(Ar.Dungeon, new object[] { owner, slot, x, y, 0 });

            /// <summary>입구 배치 지형 검사(3×3)를 통과하는 칸 하나. 지형은 시드 노이즈라 고정 순서로 찾는다.</summary>
            public Vector2Int FindClearEntranceTile()
            {
                // 광맥 청크 탐색(양의 y)과 겹치지 않는 음의 y 줄에서 찾는다
                int y = -(_oreChunkBounds.yMax / 2) * OreChunkSize;
                for (int x = (_oreChunkBounds.xMin + 2) * OreChunkSize; x < (_oreChunkBounds.xMax - 2) * OreChunkSize; x++)
                {
                    bool clear = true;
                    for (int dx = -1; dx <= 1 && clear; dx++)
                        for (int dy = -1; dy <= 1 && clear; dy++)
                            clear = Ar.WorldMap.IsEntranceTileClear(x + dx, y + dy);
                    if (clear) return new Vector2Int(x, y);
                }
                throw new InvalidOperationException("no clear entrance fixture tile");
            }

            public void Dispose()
            {
                _singleton.SetValue(null, _previousAr); InvalidateComponentCaches();
                UnityEngine.Object.DestroyImmediate(_go);
                DeleteDiagnosticDirectory(_path);
            }

            private static T Private<T>(object owner, string name)
                => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);

            private static void InvalidateComponentCaches()
            {
                var invalidators = (List<Action>)typeof(ComponentManager).GetField("_cacheInvalidators", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                for (int i = 0; i < invalidators.Count; i++) invalidators[i]();
            }
        }
    }
}
