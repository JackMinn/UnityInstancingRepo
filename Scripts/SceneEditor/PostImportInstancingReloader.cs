#if (UNITY_EDITOR) 

using UnityEditor;
namespace JacksInstancing
{
    public class PostImportInstancingReloader : AssetPostprocessor
    {

        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            bool forceReload = false;

            //If its a script reload or a scene save, then we ignore it as other functions take care of that
            for (int i = 0; i < importedAssets.Length; i++)
            {
                forceReload = forceReload | (importedAssets[i].EndsWith(".cs") || importedAssets[i].EndsWith(".js") || importedAssets[i].EndsWith(".unity"));
            }

            if (!forceReload)
                SceneViewInstancing.ReloadThis();
        }
    }
}

#endif
