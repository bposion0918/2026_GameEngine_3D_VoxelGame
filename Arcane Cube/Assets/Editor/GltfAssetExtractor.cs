#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VoxelLessons.Editor
{
    public static class GltfAssetExtractor
    {
        private const uint GlbMagic = 0x46546C67;
        private const uint JsonChunk = 0x4E4F534A;
        private const uint BinaryChunk = 0x004E4942;

        [Serializable] private sealed class GltfRoot { public GltfImage[] images; public GltfBufferView[] bufferViews; }
        [Serializable] private sealed class GltfImage { public int bufferView = -1; public string mimeType; public string name; }
        [Serializable] private sealed class GltfBufferView { public int byteOffset; public int byteLength; }

        [MenuItem("Assets/VoxEdit/GLB 텍스처와 머티리얼 추출", false, 2000)]
        private static void ExtractSelected()
        {
            string glbPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (!IsGlb(glbPath)) return;

            try
            {
                string root = (Path.GetDirectoryName(glbPath) + "/Extracted").Replace('\\', '/');
                string textureFolder = root + "/Textures";
                string materialFolder = root + "/Materials";
                EnsureFolder(textureFolder);
                EnsureFolder(materialFolder);

                Dictionary<string, string> paths = ExtractPngFiles(glbPath, textureFolder);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                ConfigureTextures(paths.Values);
                Dictionary<string, Material> materials = CreateMaterials(glbPath, materialFolder, paths);
                int replaced = ReplaceOpenSceneMaterials(materials);
                AssetDatabase.SaveAssets();
                EditorSceneManager.SaveOpenScenes();

                UnityEngine.Object folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(root);
                Selection.activeObject = folder;
                EditorGUIUtility.PingObject(folder);
                EditorUtility.DisplayDialog("추출 완료",
                    $"PNG {paths.Count}개, Material {materials.Count}개를 만들었습니다.\n" +
                    $"현재 장면의 Material 슬롯 {replaced}개를 교체했습니다.\n\n{root}", "확인");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("추출 실패", exception.Message, "확인");
            }
        }

        [MenuItem("Assets/VoxEdit/GLB 텍스처와 머티리얼 추출", true)]
        private static bool ValidateExtractSelected() => IsGlb(AssetDatabase.GetAssetPath(Selection.activeObject));

        private static bool IsGlb(string path) => !string.IsNullOrEmpty(path)
            && string.Equals(Path.GetExtension(path), ".glb", StringComparison.OrdinalIgnoreCase);

        private static Dictionary<string, string> ExtractPngFiles(string glbPath, string folder)
        {
            using FileStream stream = File.OpenRead(Path.GetFullPath(glbPath));
            using BinaryReader reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != GlbMagic) throw new InvalidDataException("올바른 GLB 파일이 아닙니다.");
            uint version = reader.ReadUInt32();
            reader.ReadUInt32();
            if (version != 2) throw new NotSupportedException("GLB 2.0만 지원합니다.");

            int jsonLength = checked((int)reader.ReadUInt32());
            if (reader.ReadUInt32() != JsonChunk) throw new InvalidDataException("JSON 청크를 찾지 못했습니다.");
            string json = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(jsonLength)).TrimEnd('\0', ' ', '\t', '\r', '\n');
            int binaryLength = checked((int)reader.ReadUInt32());
            if (reader.ReadUInt32() != BinaryChunk) throw new InvalidDataException("BIN 청크를 찾지 못했습니다.");
            byte[] binary = reader.ReadBytes(binaryLength);

            GltfRoot gltf = JsonUtility.FromJson<GltfRoot>(json);
            if (gltf?.images == null || gltf.bufferViews == null) throw new InvalidDataException("내장 이미지를 찾지 못했습니다.");

            Dictionary<string, string> result = new Dictionary<string, string>();
            for (int index = 0; index < gltf.images.Length; index++)
            {
                GltfImage image = gltf.images[index];
                if (image.bufferView < 0 || image.bufferView >= gltf.bufferViews.Length) continue;
                if (!string.IsNullOrEmpty(image.mimeType) && image.mimeType != "image/png") continue;
                GltfBufferView view = gltf.bufferViews[image.bufferView];
                byte[] bytes = new byte[view.byteLength];
                Buffer.BlockCopy(binary, view.byteOffset, bytes, 0, view.byteLength);
                string name = string.IsNullOrWhiteSpace(image.name) ? $"image_{index}" : SafeName(image.name);
                string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{name}.png");
                File.WriteAllBytes(Path.GetFullPath(path), bytes);
                result[$"image_{index}"] = path;
                result[name] = path;
            }
            return result;
        }

        private static void ConfigureTextures(IEnumerable<string> paths)
        {
            foreach (string path in paths.Distinct())
            {
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
                importer.textureType = TextureImporterType.Default;
                importer.filterMode = FilterMode.Point;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.SaveAndReimport();
            }
        }

        private static Dictionary<string, Material> CreateMaterials(
            string glbPath, string folder, IReadOnlyDictionary<string, string> texturePaths)
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(glbPath);
            Dictionary<string, Material> result = new Dictionary<string, Material>();
            foreach (Material source in assets.OfType<Material>())
            {
                Material copy = new Material(source) { name = source.name };
                foreach (string property in copy.GetTexturePropertyNames())
                {
                    Texture sourceTexture = source.GetTexture(property);
                    if (sourceTexture == null || !texturePaths.TryGetValue(sourceTexture.name, out string texturePath)) continue;
                    Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                    if (texture != null) copy.SetTexture(property, texture);
                }
                string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{SafeName(source.name)}.mat");
                AssetDatabase.CreateAsset(copy, path);
                result[source.name] = copy;
            }
            return result;
        }

        private static int ReplaceOpenSceneMaterials(IReadOnlyDictionary<string, Material> replacements)
        {
            int count = 0;
            foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                Material[] slots = renderer.sharedMaterials;
                bool changed = false;
                for (int index = 0; index < slots.Length; index++)
                {
                    Material current = slots[index];
                    if (current == null || !replacements.TryGetValue(current.name, out Material replacement)) continue;
                    slots[index] = replacement;
                    changed = true;
                    count++;
                }
                if (!changed) continue;
                Undo.RecordObject(renderer, "VoxEdit 외부 Material 연결");
                renderer.sharedMaterials = slots;
                EditorUtility.SetDirty(renderer);
            }
            return count;
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }

        private static string SafeName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return value;
        }
    }
}
#endif