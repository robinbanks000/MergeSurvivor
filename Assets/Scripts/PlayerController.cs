using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public float moveSpeed = 5f;
    public GameObject bulletPrefab;
    public Transform firePoint;

    void Update()
    {
        // Bevægelse (venstre/højre)
        float move = Input.GetAxis("Horizontal");
        transform.Translate(Vector3.right * move * moveSpeed * Time.deltaTime);

        // Skyd
        if (Input.GetKeyDown(KeyCode.Space))
        {
            Shoot();
        }
    }

    void Shoot()
    {
        Instantiate(bulletPrefab, firePoint.position, Quaternion.identity);
    }
}
