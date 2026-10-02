using UnityEngine;

public class DroneMovementController : MonoBehaviour
{
    [SerializeField]
    private float speed = 0.5f;

    private void Update()
    {
        if (Input.GetKey(KeyCode.Q))
        {
            gameObject.transform.Translate(Vector3.up * speed * Time.deltaTime);
        }
        else if (Input.GetKey(KeyCode.E))
        {
            gameObject.transform.Translate(Vector3.down * speed * Time.deltaTime);
        }
        else if(Input.GetKey(KeyCode.A))
        {
            gameObject.transform.Translate(Vector3.left * speed * Time.deltaTime);
        }
        else if (Input.GetKey(KeyCode.D))
        {
            gameObject.transform.Translate(Vector3.right * speed * Time.deltaTime);
        }
        else if (Input.GetKey(KeyCode.W))
        {
            gameObject.transform.Translate(Vector3.forward * speed * Time.deltaTime);
        }
        else if (Input.GetKey(KeyCode.S))
        {
            gameObject.transform.Translate(Vector3.back * speed * Time.deltaTime);
        }
    }
}
