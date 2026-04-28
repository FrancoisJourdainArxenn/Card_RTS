using UnityEngine;
using System.Collections;

public class DamageEffectVisual : MonoBehaviour {

    public GameObject DamagePrefab;
    public static DamageEffectVisual Instance;

    void Awake()
    {
        Instance = this;
    }

    void Update()
    {
        // if (Input.GetKeyDown(KeyCode.A))
        //     DamageEffect.CreateDamageEffect(transform.position, Random.Range(-7, 7));
    }
}
