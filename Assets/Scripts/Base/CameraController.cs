#nullable enable
using ARPG.Base;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class CameraController : MonoBehaviour
{
    [SerializeField] private Camera _camera;

    private EntityBase? _player;

    public void Initialize(EntityBase inPlayer)
    {
        _player = inPlayer;

        //SetupCameraStack();
    }

    private void SetupCameraStack()
    {
        var cameraData = _camera.GetUniversalAdditionalCameraData();

        Camera? uiCamera = ARPG.AR.s?.UI?.UICamera;
        if (cameraData != null)
        {
            cameraData.cameraStack.Add(uiCamera);

            Debug.Log("UI Camera added to camera stack");
        }
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
