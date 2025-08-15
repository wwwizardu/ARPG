#nullable enable
using ARPG.Creature;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    [SerializeField] private Camera _camera;

    private ArpgPlayer? _player;

    public void Initialize(ArpgPlayer inPlayer)
    {
        _player = inPlayer;
    }

    // Update is called once per frame
    void Update()
    {
        if (_player == null)
            return;

        // 카메라가 플레이어를 따라다니도록 설정
        Vector3 targetPosition = _player.transform.position;
        targetPosition.z = _camera.transform.position.z; // 카메라의 z값 유지
        _camera.transform.position = targetPosition;
    }
}
