using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HelloWorld
{
    public class HelloWorldPlayer : NetworkBehaviour
    {
        public NetworkVariable<Vector3> Position = new NetworkVariable<Vector3>();
		private Rigidbody rb;

        public override void OnNetworkSpawn()
        {
            rb=GetComponent<Rigidbody>();
            if (IsOwner)
            {
                Move();
            }
        }

        public void Move()
        {
            SubmitPositionRequestServerRpc();
        }

        [ServerRpc]
        private void SubmitPositionRequestServerRpc()
        {
            var randomPosition = GetRandomPositionOnPlane();
            transform.position = randomPosition;
            Position.Value = randomPosition;
        }

        
        static Vector3 GetRandomPositionOnPlane()
        {
            
            return new Vector3(Random.Range(-3f, 3f), 1f, Random.Range(-3f, 3f));
        }

        
        private void FixedUpdate()
        {
            if (!IsOwner)
            {
                if (rb != null)
                {
                    rb.MovePosition(Position.Value);
                }
                
            }
            
        }
    }
}