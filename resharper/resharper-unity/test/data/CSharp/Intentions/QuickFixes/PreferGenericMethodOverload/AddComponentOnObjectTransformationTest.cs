using UnityEngine;

public class Whatever : MonoBehaviour
{
        
}

public class Test02
{
    public void Method(Transform o)
    {
        o.gameObject.AddComponent("Whate{caret}ver");
    }
}
