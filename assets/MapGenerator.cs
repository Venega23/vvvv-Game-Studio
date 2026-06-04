using UnityEngine;

[ExecuteAlways] // РџРѕР·РІРѕР»СЏРµС‚ СЃРєСЂРёРїС‚Сѓ СЂР°Р±РѕС‚Р°С‚СЊ РІ СЂРµР¶РёРјРµ СЂРµРґР°РєС‚РѕСЂР°
public class MapGenerator : MonoBehaviour
{
    public GameObject plane;
    public Material wallMaterial;
    public Material pillarMaterial;
    public float wallHeight = 3f;
    public float wallThickness = 0.5f;
    public int pillarCount = 20;

    private void Start()
    {
        GenerateMap();
    }

    // Р­С‚РѕС‚ РјРµС‚РѕРґ РІС‹Р·С‹РІР°РµС‚СЃСЏ Р°РІС‚РѕРјР°С‚РёС‡РµСЃРєРё, РєРѕРіРґР° С‚С‹ РјРµРЅСЏРµС€СЊ С‡С‚Рѕ-С‚Рѕ РІ РёРЅСЃРїРµРєС‚РѕСЂРµ
    private void OnValidate()
    {
        // РСЃРїРѕР»СЊР·СѓРµРј Р·Р°РґРµСЂР¶РєСѓ, С‡С‚РѕР±С‹ РЅРµ РІС‹Р·С‹РІР°С‚СЊ РѕС€РёР±РєРё РїСЂРё РёР·РјРµРЅРµРЅРёРё РёРµСЂР°СЂС…РёРё
        UnityEditor.EditorApplication.delayCall += () => {
            if (this != null) GenerateMap();
        };
    }

    public void GenerateMap()
    {
        if (plane == null) return;

        ClearGeneratedObjects(); // РўРµРїРµСЂСЊ СЌС‚РѕС‚ РјРµС‚РѕРґ СЃСѓС‰РµСЃС‚РІСѓРµС‚!

        Bounds bounds = plane.GetComponent<Renderer>().bounds;
        Vector3 size = bounds.size;
        Vector3 center = bounds.center;

        // РЎРѕР·РґР°РµРј 4 СЃС‚РµРЅС‹
        CreateWall(new Vector3(center.x, center.y + wallHeight / 2, bounds.max.z), new Vector3(size.x, wallHeight, wallThickness)); // РџРµСЂРµРґРЅСЏСЏ
        CreateWall(new Vector3(center.x, center.y + wallHeight / 2, bounds.min.z), new Vector3(size.x, wallHeight, wallThickness)); // Р—Р°РґРЅСЏСЏ
        CreateWall(new Vector3(bounds.max.x, center.y + wallHeight / 2, center.z), new Vector3(wallThickness, wallHeight, size.z)); // РџСЂР°РІР°СЏ
        CreateWall(new Vector3(bounds.min.x, center.y + wallHeight / 2, center.z), new Vector3(wallThickness, wallHeight, size.z)); // Р›РµРІР°СЏ

        // РЎРѕР·РґР°РµРј РєРѕР»РѕРЅРЅС‹
        for (int i = 0; i < pillarCount; i++)
        {
            Vector3 randomPos = new Vector3(
                Random.Range(bounds.min.x + 2, bounds.max.x - 2),
                center.y + wallHeight / 2,
                Random.Range(bounds.min.z + 2, bounds.max.z - 2)
            );
            GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pillar.transform.position = randomPos;
            pillar.transform.localScale = new Vector3(1.5f, wallHeight, 1.5f);
            pillar.transform.parent = this.transform;
            if (pillarMaterial != null) pillar.GetComponent<Renderer>().material = pillarMaterial;
        }
    }

    void CreateWall(Vector3 pos, Vector3 scale)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = pos;
        wall.transform.localScale = scale;
        wall.transform.parent = this.transform;
        if (wallMaterial != null) wall.GetComponent<Renderer>().material = wallMaterial;
    }

    void ClearGeneratedObjects()
    {
        // РЈРґР°Р»СЏРµРј РІСЃРµ РґРѕС‡РµСЂРЅРёРµ РѕР±СЉРµРєС‚С‹ (СЃС‚РµРЅС‹ Рё РєРѕР»РѕРЅРЅС‹)
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            if (Application.isPlaying)
                Destroy(transform.GetChild(i).gameObject);
            else
                DestroyImmediate(transform.GetChild(i).gameObject);
        }
    }
}
