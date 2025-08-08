#nullable enable

/*
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BSNClient
{
    public class UIResource
    {
        public AsyncOperationHandle Handle;
        public UIBase? UI;
    }

    public class UIManager : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public static int Width = 1920;
        public static int Height = 1080;

        public enum Layer
        {
            Popup,
            Tooltip,
            Top,
            Main,
            MainBase,
        }

        public enum PopupType
        {
            Confirm,
            YesNo,
        }

        public enum TooltipType
        {
            Item,
            Recipe,
            Zone,
            Merchant,
        }

        public enum BsnSceneType
        {
            TitleScene,
            GameScene,
        }

        [Header("Camera")]
        [SerializeField] private Camera _uiCamera;

        [Header("Canvas")]
        [SerializeField] private Transform _topCanvas;
        [SerializeField] private Transform _mainCanvas;
        
        [Header("Loading UI")]
        [SerializeField] private UnityEngine.UI.Image _fadeInOutBG;
        [SerializeField] private TextMeshProUGUI _loadingText;

        [Header("Layer Root")]
        [SerializeField] private Transform _topRoot;
        [SerializeField] private Transform _popupRoot;
        [SerializeField] private Transform _mainBase;

        [Header("Root")]
        [SerializeField] private Transform _objectRoot;

        [Header("Waiting For Response")]
        [SerializeField] private GameObject _waitingFor;

        [Header("Drag And Drop Slot")]
        [SerializeField] private DummySlotUI _dummySlot;

        [Header("BG")]
        [SerializeField] private GameObject _bgImage;


        private Dictionary<string, UIResource> _uiList = new Dictionary<string, UIResource>();
        private List<UIBase> _uiBaseList = new List<UIBase>(); // 항상 떠있어야 하는 UI는 이곳에 넣는다.
        private List<UIBase> _uiCurrentList = new List<UIBase>(); // 현재 활성화된 UI 리스트

        private float _topCanvasCameraPlanDistance = 0f;
        private float _mainCanvasCameraPlanDistance = 0f;

        private GraphicRaycaster _topLayerRaycaster;
        private GraphicRaycaster _mainLayerRaycaster;

        private PointerEventData _pointerEventData;

        private TooltipUI? _tooltip = null;
        private Coroutine? _tooltipCoroutine = null;

        private bool _isLocked = false;

        private bool _isEventActivate = false; // 이벤트가 활성화되었는지 여부

        private bool _isPressed = false;
        private float _pressedTime = 0f;

        private bool _isDragging = false;

        private bool _isGameScene = false;

        private Canvas? _canvas;
        private Vector3[] _corners = new Vector3[4];

        private InputController.BsnInput _input = new InputController.BsnInput();
        
        private InputSystem_BSN _inputSystem;

        private Coroutine? _saveCoroutine = null;

        public Camera UICamera { get { return _uiCamera; } }

        public RectTransform MainCanvas { get { return (RectTransform)_mainCanvas; } }

        public RectTransform TopCanvas { get { return (RectTransform)_topCanvas; } }
        public Transform ObjectRoot { get { return _objectRoot; } }

        public UIBase? CurrentUI { get { return _uiCurrentList.Count == 0 ? null : _uiCurrentList[_uiCurrentList.Count - 1]; } }

        public bool IsLock { get { return _isLocked; } }

        public bool IsPressed { get { return _isPressed == true && 0.5f < _pressedTime; } }

        public InputController.BsnInput Input { get { return _input; } }    

        public bool IsUIMode { get { return _input.IsUIMode; } }

        public Action OnLangugeChanged;

        public DummySlotUI DummySlot { get { return _dummySlot; } }

        void Awake()
        {
            _uiBaseList.Clear();
            _uiCurrentList.Clear();

            _uiCamera!.orthographicSize = Screen.height * 0.5f;

            _canvas = _mainCanvas.GetComponent<Canvas>();
            _topCanvasCameraPlanDistance = _topCanvas.GetComponent<Canvas>().planeDistance;
            _mainCanvasCameraPlanDistance = _mainCanvas.GetComponent<Canvas>().planeDistance;

            _topLayerRaycaster = _topCanvas.GetComponent<GraphicRaycaster>();
            _mainLayerRaycaster = _mainCanvas.GetComponent<GraphicRaycaster>();

            _pointerEventData = new PointerEventData(EventSystem.current);

            _inputSystem = new InputSystem_BSN();
            _inputSystem.Enable();
            _input.Initialize(_inputSystem);
        }

        public void DisableInputSystem()
        {
            if (_inputSystem == null)
            {
                return;
            }

            if (_inputSystem.Player.enabled)
            {
                _inputSystem.Player.Disable();
            }

            if (_inputSystem.UI.enabled)
            {
                _inputSystem.UI.Disable();
            }

            _inputSystem.Disable();
        }

        public T? Get<T>(string inName, Layer inLayer = Layer.Main) where T : UIBase
        {
            if (_uiList.ContainsKey(inName) == false)
            {
                T ui = LoadUI<T>(inName, inLayer);
                if (ui == null)
                {
                    Debug.LogError($"[UIManager] Show - UI({inName}) is not found.");
                    return null;
                }

                _uiList[inName].UI?.gameObject.SetActive(false);
            }

            return _uiList[inName].UI as T;
        }

        public T? Show<T>(string inName, Layer inLayer) where T : UIBase
        {
            Lock();

            // 지금 이벤트를 받고 있는 UI 초기화
            CurrentUI?.CancelPointerEvent();

            if (IsShow(inName) == true)
            {
                if (0 < _uiCurrentList.Count && _uiCurrentList[_uiCurrentList.Count - 1].Name == inName) // 최상위 UI라면 바로 리턴
                {
                    UnLock();
                    return _uiCurrentList[_uiCurrentList.Count - 1] as T;
                }

                if(_uiList.TryGetValue(inName, out UIResource outUI) == false)
                {
                    Debug.LogError($"[UIManager] Show() - not found in _uiList, inName({inName})");
                    UnLock();
                    return null;
                }

                if(outUI.UI.IsBase == true) // Base UI라면 그냥 리턴한다.
                {
                    UnLock();
                    return outUI.UI as T;
                }
                else
                {
                    if(Hub.isUnderGround == true && _isGameScene == true)
                    {
                        _bgImage.SetActive(true);
                    }
                    
                    // 그 밑에 있는 UI라면 최상위로 위치를 옮긴다.
                    int findIndex = _uiCurrentList.IndexOf(_uiList[inName].UI);
                    if (findIndex == -1)
                    {
                        Debug.LogError($"[UIManager] Show() - not found in _uiCurrentList, inName({inName})");
                        UnLock();
                        return null;
                    }

                    T? uiTemp = _uiCurrentList[findIndex] as T;
                    if (uiTemp == null)
                    {
                        Debug.LogError($"[UIManager] Show() - not found in _uiCurrentList, inName({inName})");
                        UnLock();
                        return null;
                    }

                    // 기존 리스트에서 빼서 맨 마지막으로 이동시킨다.
                    _uiCurrentList.RemoveAt(findIndex);
                    _uiCurrentList.Add(uiTemp);

                    UnLock();
                    return uiTemp;
                }
            }

            T ui = LoadUI<T>(inName, inLayer);
            if (ui == null)
            {
                Debug.LogError($"[UIManager] Show - UI({inName}) is not found.");
                UnLock();
                return null;
            }

            if(ui.IsBase == true)
            {
                if (_uiBaseList.Contains(ui) == false)
                {
                    _uiBaseList.Add(ui);
                }
            }
            else
            {
                if (Hub.isUnderGround == true && _isGameScene == true)
                {
                    _bgImage.SetActive(true);
                }

                if (_uiCurrentList.Contains(ui) == false)
                {
                    _uiCurrentList.Add(ui);
                }
            }

            UnLock(); // ***** 주의 - 아래의 ui.OnOpen()함수에서 EnableCtrlSkip값을 세팅하기 때문에 이 위치에 있어야 한다 *****

            ui.gameObject.SetActive(true);
            ui.OnOpen();

            // _uiCurrentList.Count가 0보다 크면 Input ActionMap을 UI로 세팅, 아니라면 Player 타입으로 세팅
            UpdateInputActionMap();

            return ui;
        }

        //public async void ShowPopup(PopupData inPopupData, Action<string, PopupButtonData> onClicked, bool isAutoPlay)
        //{
        //    if (inPopupData == null)
        //    {
        //        Debug.LogError("[UIManager] ShowPopup - inPopupData is null");
        //        return;
        //    }

        //    UIPopup popup = await Show<UIPopup>("UI/UIPopup", Layer.Top);
        //    if (popup != null)
        //    {
        //        popup.SetPopupData(inPopupData, onClicked, isAutoPlay);
        //    }
        //}

        //public async void ShowPopup(string inScenarioName, string inValue, Action<string, PopupButtonData> onClicked, bool isAutoPlay)
        //{
        //    Lock();

        //    UIPopup popup = await Show<UIPopup>("UI/UIPopup", Layer.Popup);
        //    if (popup != null)
        //    {
        //        popup.SetPopupData(inScenarioName, inValue, onClicked, isAutoPlay);
        //    }
        //}

        //public void CloseAllPopup()
        //{
        //    for (int i = _uiCurrentList.Count - 1; i >= 0; i--)
        //    {
        //        if (_uiCurrentList[i] is UIPopup)
        //        {
        //            _uiCurrentList[i].gameObject.SetActive(false);
        //            _uiCurrentList.RemoveAt(i);
        //        }
        //    }

        //    // 스킵 활성화 체크
        //    EnableCtrlSkip = true;
        //    for (int i = 0; i < _uiCurrentList.Count; i++)
        //    {
        //        if (_uiCurrentList[i].EnableCtrlSkip == false)
        //        {
        //            EnableCtrlSkip = false;
        //            break;
        //        }
        //    }
        //}

        // 툴팁은 따로 관리한다. 기존의 Show함수를 이용하면 tooltip UI에 이벤트가 들어가기 때문에 문제가 발생함
        public void ShowRecipeTooltip(int inRecipeMasterId, RectTransform inBaseUI)
        {
            if (_tooltipCoroutine != null)
            {
                StopCoroutine(_tooltipCoroutine);
            }

            RecipeTooltipUI? recipeTooltip = Get<RecipeTooltipUI>(AddressablePath.RecipeTooltipUI, Layer.Tooltip);
            if (recipeTooltip == null)
            {
                Debug.LogError($"[UIManager] ShowTooltip - _tooltip({AddressablePath.RecipeTooltipUI}) is null");
                return;
            }

            recipeTooltip.gameObject.SetActive(true);
            recipeTooltip.SetRecipeData(inRecipeMasterId);
            recipeTooltip.OnOpen();

            // 대상 UI의 월드 좌표에서 모든 모서리 가져오기
            inBaseUI.GetWorldCorners(_corners);
            Vector3 topRightCorner = _corners[2]; // 우상단 모서리
            Vector3 topLeftCorner = _corners[1];  // 좌상단 모서리

            _tooltip = recipeTooltip;
            _tooltipCoroutine = StartCoroutine(ShowTooltipCo(topRightCorner, topLeftCorner));
        }

        public void ShowTooltip(string inName, Bifrost.Cooked.ItemInfo? inItemInfo, int inDurability, RectTransform inBaseUI)
        {
            // 대상 UI의 월드 좌표에서 모든 모서리 가져오기
            inBaseUI.GetWorldCorners(_corners);
            Vector3 topRightCorner = _corners[2]; // 우상단 모서리
            Vector3 topLeftCorner = _corners[1];  // 좌상단 모서리

            ShowTooltip(inName, inItemInfo, inDurability, topRightCorner, topLeftCorner);
        }

        public void ShowTooltip(string inName, Bifrost.Cooked.ItemInfo? inItemInfo, int inDurability, Vector3 inScreenPosition, Vector3 inScreenLeftPosition)
        {
            if (_tooltipCoroutine != null)
            {
                StopCoroutine(_tooltipCoroutine);
            }

            _tooltip = Get<TooltipUI>(inName, Layer.Tooltip);
            if (_tooltip == null)
            {
                Debug.LogError($"[UIManager] ShowTooltip - _tooltip({inName}) is null");
                return;
            }

            _tooltip.gameObject.SetActive(true);
            _tooltip.SetItemTooltip(inItemInfo, inDurability);
            _tooltip.OnOpen();

            _tooltipCoroutine = StartCoroutine(ShowTooltipCo(inScreenPosition, inScreenLeftPosition));
        }

        public void HideTooltip()
        {
            if (_tooltip == null || IsShow(_tooltip.Name) == false)
                return;

            if (_tooltipCoroutine != null)
            {
                StopCoroutine(_tooltipCoroutine);
            }
            _tooltipCoroutine = null;

            _tooltip.gameObject.SetActive(false);
            _tooltip.OnClose();
        }

        //public void HideTooltip()
        //{
        //    for (int i = 0; i < _uiTooltipList.Count; i++)
        //    {
        //        _uiTooltipList[i].Close();
        //    }
        //}

        public bool IsMouseOverItem()
        {
            // 현재 마우스 위치 가져오기
            PointerEventData pointerEventData = new PointerEventData(EventSystem.current);
            pointerEventData.position = _input.MousePosition;

            // 레이캐스트로 UI 요소 감지
            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointerEventData, results);

            // 감지된 UI 요소들 중 하나라도 대상 uiElement와 일치하는지 확인
            foreach (RaycastResult result in results)
            {
                var slot = result.gameObject.GetComponent<SlotUI>();
                if (slot != null)
                {
                    if (slot.CurrentItem != null)
                        return true;
                }
            }

            return false; // 마우스가 해당 UI 요소 위에 없음
        }

        public void Close(string inName, bool isDestroy = false)
        {
            //RGM.Instance.UIMgr.HideTooltip();

            if (string.IsNullOrEmpty(inName) == true || _uiList.ContainsKey(inName) == false)
            {
                Debug.LogWarning($"[UIManager] Close - UI({inName}) is not found.");
                return;
            }

            UIBase? ui = _uiList[inName].UI;
            if(ui != null) 
            {
                ui.OnClose();
                ui.gameObject.SetActive(false);

                if (ui.IsBase == false)
                {
                    _uiCurrentList.Remove(ui);
                }

                if (_uiCurrentList.Count == 0) // 더이상 활성화된 UI가 없다면 배경 이미지를 끈다.
                {
                    _bgImage.SetActive(false);
                }

                // UI 삭제
                if (isDestroy == true)
                {
                    if (_uiList.ContainsKey(inName) == true)
                    {
                        _uiList[inName].UI?.transform.SetParent(null);
                        _uiList[inName].UI = null;
                        Addressables.ReleaseInstance(_uiList[inName].Handle);

                        _uiList.Remove(inName);
                    }
                }
            }

            

            // 창이 닫힐 때 툴팁도 닫는걸 시도한다.
            HideTooltip();

            // _uiCurrentList.Count가 0보다 크면 Input ActionMap을 UI로 세팅, 아니라면 Player 타입으로 세팅
            UpdateInputActionMap();

            Hub.s!.pdata.OwnerPlayer?.OnClosedUI();
        }

        public void SetIsGameScene(bool isGameScene)
        {
            _isGameScene = isGameScene;
        }

        public bool IsShow(string inName)
        {
            if (_uiList.ContainsKey(inName) == false)
                return false;

            return _uiList[inName].UI.gameObject.activeSelf;
        }

        public void UpdateInputActionMap() 
        {
            if(Hub.s!.console.IsActivateDebugConsol == true)
            {
                _input.ChangeActionMap(true);
            }
            else if (Hub.isUnderGround == false)
            {
                _input.ChangeActionMap(true);
            }
            else
            {
                // _uiCurrentList.Count가 0보다 크면 Input ActionMap을 UI로 세팅, 아니라면 Player 타입으로 세팅
                _input.ChangeActionMap(0 < _uiCurrentList.Count);
            }
        }

        public void OnChangeScene(BsnSceneType inSceneType)
        {
            switch (inSceneType)
            {
                case BsnSceneType.TitleScene:
                    break;
                case BsnSceneType.GameScene:
                    Hub.s!.uiman.UpdateInputActionMap();
                    break;
            }
        }

        public void Lock()
        {
            _isLocked = true;
        }

        public void UnLock()
        {
            _isLocked = false;
        }

        public void EventActivate(bool Activate)
        {
            _isEventActivate = Activate;
        }

        public bool IsEventActivate()
        {
            return _isEventActivate;
        }


        public Vector3 ScreenToWorldPoint(Vector3 inScreenPosition, Layer inLayer)
        {
            if (inLayer == Layer.Top)
            {
                inScreenPosition.z = _topCanvasCameraPlanDistance;
            }
            else if (inLayer == Layer.Main || inLayer == Layer.MainBase)
            {
                inScreenPosition.z = _mainCanvasCameraPlanDistance;
            }

            return _uiCamera!.ScreenToWorldPoint(inScreenPosition);
        }

        public Vector2 WorldToScreenPoint(Vector3 inWorldPosition)
        {
            return _uiCamera!.WorldToScreenPoint(inWorldPosition);
        }

        public IEnumerator LoadingStart()
        {
            // 로딩 전 모든 이펙트를 제거한다(Scene이 교체되는 경우 Addressables.InstantiateAsync()함수로 로드했던 오브젝트들이 사라지면서 붙어있던 이펙트가 null이 될 수 있음)
            //RGM.Instance.EffectMgr.ClearAllEffect();
            StopShowSaveText();

            FadeIn(0.3f);
            yield return new WaitForSeconds(0.3f);

            _loadingText.text = "Loading...";
            _loadingText.gameObject.SetActive(true);
            _loadingText.color = Color.white;

            ClearAll();
        }

        public void Save()
        {
            _loadingText.text = "Saving...";
            _loadingText.gameObject.SetActive(true);
        }

        public void SaveComplete()
        {
            if (_saveCoroutine != null)
            {
                StopCoroutine(_saveCoroutine);
            }

            _saveCoroutine = StartCoroutine(ShowSaveText());
        }

        public async UniTask LoadingTitle(float inSecond)
        {
            AsyncOperationHandle<Sprite> handle = Addressables.LoadAssetAsync<Sprite>("UI/TitleLogo");
            Sprite titleImage = await handle.ToUniTask();
            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogError("[UIManager] LoadingTitle - handle.Status fail");
                Addressables.Release(handle);
                return;
            }

            if (titleImage == null)
            {
                Debug.LogError("[UIManager] LoadingTitle - titleImage is null");
                return;
            }

            _fadeInOutBG.sprite = titleImage;
            _fadeInOutBG.gameObject.SetActive(true);
            _fadeInOutBG.color = new Color(1f, 1f, 1f, 0f);
            _fadeInOutBG.DOColor(new Color(1f, 1f, 1f, 1f), inSecond).SetEase(Ease.InQuart);

            await UniTask.Delay((int)inSecond * 1000);

            _fadeInOutBG.color = new Color(1f, 1f, 1f, 1f);
            _fadeInOutBG.DOColor(new Color(1f, 1f, 1f, 0f), inSecond);

            await UniTask.Delay((int)inSecond * 1000);

            _fadeInOutBG.gameObject.SetActive(false);
            _fadeInOutBG.sprite = null;

            Addressables.Release(handle);
        }

        public async UniTask DisplayVersion()
        {
            _loadingText.text = $"Version(0.7.572)";
            _loadingText.gameObject.SetActive(true);

            await UniTask.Delay(TimeSpan.FromSeconds(5f));

            _loadingText.gameObject.SetActive(false);
        }

        public IEnumerator LoadingEnd()
        {
            _loadingText.gameObject.SetActive(false);

            FadeOut(0.3f);

            yield return new WaitForSeconds(0.3f);
        }

        public void FadeIn(float inDuration)
        {
            _fadeInOutBG.gameObject.SetActive(true);
            _fadeInOutBG.color = new Color(0f, 0f, 0f, 0f);

            _fadeInOutBG.DOColor(new Color(0f, 0f, 0f, 1f), inDuration);
        }

        public void FadeOut(float inDuration)
        {
            _fadeInOutBG.color = new Color(0f, 0f, 0f, 1f);
            _fadeInOutBG.DOColor(new Color(0f, 0f, 0f, 0f), inDuration).OnComplete(() => _fadeInOutBG.gameObject.SetActive(false));
        }

        public void FadeInOut(float inDuration)
        {
            _fadeInOutBG.gameObject.SetActive(true);
            _fadeInOutBG.color = new Color(0f, 0f, 0f, 0f);

            float halfDuration = inDuration * 0.5f;
            _fadeInOutBG.DOColor(new Color(0f, 0f, 0f, 1f), halfDuration).OnComplete(() => FadeOut(halfDuration));
        }

        public void MoveToNextFloor()
        {

        }

        public void OnPointerDown(PointerEventData eventData)
        {
            //if (_waitingFor.activeSelf == true)
            //    return;

            if (_isLocked == true)
                return;

            if (eventData.button == PointerEventData.InputButton.Left)
            {
                bool isAllPass = true;
                for (int i = _uiCurrentList.Count - 1; 0 <= i; i--)
                {
                    if (_uiCurrentList[i].OnPointerDown(eventData) == true)
                    {
                        isAllPass = false;
                        break;
                    }
                }

                if(isAllPass == true) // 현재 UI에서 이벤트 사용하는 부분이 없다면 Base UI도 체크한다.
                {
                    for (int i = _uiBaseList.Count - 1; 0 <= i; i--)
                    {
                        if (_uiBaseList[i].OnPointerDown(eventData) == true)
                        {
                            break;
                        }
                    }
                }
            }
                  
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            //if (_waitingFor.activeSelf == true)
            //    return;

            if (eventData.button == PointerEventData.InputButton.Left)
            {
                bool isAllPass = true;
                for (int i = _uiCurrentList.Count - 1; 0 <= i; i--)
                {
                    if (_uiCurrentList[i].OnPointerUp(eventData) == true)
                    {
                        break;
                    }
                }

                if (isAllPass == true) // 현재 UI에서 이벤트 사용하는 부분이 없다면 Base UI도 체크한다.
                {
                    for (int i = _uiBaseList.Count - 1; 0 <= i; i--)
                    {
                        if (_uiBaseList[i].OnPointerUp(eventData) == true)
                        {
                            break;
                        }
                    }
                }
            }
        }

        public void BeginDraggingFromSlot(SlotUI slot, PointerEventData eventData)
        {
            if (_input.UI.Ctrl.IsPressed() == true)
                return;

            HandleSlotClick(slot);

            CurrentUI?.OnBeginDragAndDrop(slot);
        }

        public void EndDraggingOn(PointerEventData eventData)
        {
            List<RaycastResult> results = new();
            EventSystem.current.RaycastAll(eventData, results);

            SlotUI clickedSlot = null;
            
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].gameObject.TryGetComponent(out SlotUI slot))
                {
                    clickedSlot = slot;
                    break;
                }
                
                if (results[i].gameObject.CompareTag("ItemDropZone"))
                {
                    DropItem();
                    break;
                }
            }
            
            if (clickedSlot != null)
            {
                HandleSlotClick(clickedSlot);
            }

            CurrentUI?.OnEndDragAndDrop();
        }

        public bool CheckPointerUpEmptySpace(string inName, Vector3 inPosition)
        {
            //if (_waitingFor.activeSelf == true)
            //    return false;

            // 빈 공간으로 지정된 Image가 클릭되면 그 UI를 닫는다.
            UIBase uiBase = FindEmptySpace(inPosition);
            if (uiBase == null)
                return false;

            if (inName == uiBase.Name)
                return true;
            return false;
        }

        public void DropItem()
        {
            if (_dummySlot.HasItem() == false)
                return;

            if (Hub.isUnderGround == false)
                return;

            Player? player = Hub.s?.pdata?.OwnerPlayer;
            if (player == null)
                return;

            if (_dummySlot.CurrentItem == null)
                return;

            Vector2 pos = new Vector2(player.transform.position.x, player.transform.position.y);
            _dummySlot.TargetInventory.DropItemBySlotRpc(_dummySlot.TargetInventory, 0, pos);
        }

        public Vector2 GetPositionTooltip(Vector3 inScreenPosition, Vector3 inScreenLeftPosition)
        {
            if (_tooltip == null)
                return Vector2.zero;

            // Canvas 컴포넌트와 RectTransform 가져오기
            RectTransform? canvasRect = _mainCanvas as RectTransform;

            // 화면 좌표를 Canvas 로컬 좌표로 변환
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                inScreenPosition,
                null, // Screen Space - Overlay에서는 카메라가 null
                out Vector2 localPoint
            );

            // 기본 오프셋 적용
            Vector2 offset = new Vector2(5f, 0f);
            Vector2 tooltipPosition = localPoint + offset;

            // 툴팁 크기 가져오기
            var tooltipRect = _tooltip.TooltipRect;
            Vector2 tooltipSize = tooltipRect.sizeDelta;

            // Canvas 크기 가져오기
            Vector2 canvasSize = canvasRect!.sizeDelta;

            // Width 경계 체크 및 조정
            float tooltipRightEdge = tooltipPosition.x + tooltipSize.x;
            float canvasRightEdge = canvasSize.x * 0.5f; // Canvas 중심 기준 오른쪽 경계

            if (tooltipRightEdge > canvasRightEdge)
            {
                // 대상 UI의 좌상단 모서리로 변경하고 툴팁 너비만큼 왼쪽으로 이동
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect,
                    inScreenLeftPosition,
                    null,
                    out Vector2 leftLocalPoint
                );
                tooltipPosition.x = leftLocalPoint.x - tooltipSize.x - offset.x;
            }

            // Height 경계 체크 및 조정
            float tooltipTopEdge = tooltipPosition.y;
            float tooltipBottomEdge = tooltipPosition.y - tooltipSize.y;
            float canvasTopEdge = canvasSize.y * 0.5f; // Canvas 중심 기준 위쪽 경계
            float canvasBottomEdge = -canvasSize.y * 0.5f; // Canvas 중심 기준 아래쪽 경계

            // 위쪽으로 나가는 경우
            if (tooltipTopEdge > canvasTopEdge)
            {
                tooltipPosition.y = canvasTopEdge;
            }
            // 아래쪽으로 나가는 경우
            else if (tooltipBottomEdge < canvasBottomEdge)
            {
                tooltipPosition.y = canvasBottomEdge + tooltipSize.y + 5;
            }

            return tooltipPosition;
        }

        private T? LoadUI<T>(string inName, Layer inLayer) where T : UIBase
        {
            Transform layerParent = GetLayer(inLayer);
            if (layerParent == null)
            {
                Debug.LogError($"[UIManager] Show - Layer({inLayer}) is not found.");
                return null;
            }

            T? uiBase = null;
            if (_uiList.ContainsKey(inName) == false)
            {
                AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(inName, layerParent);
                var result = handle.WaitForCompletion();

                if (result == null)
                {
                    Debug.LogError($"[UIManager] LoadUI - result is null, Name({inName})");
                    return null;
                }

                uiBase = handle.Result.GetComponent<T>();
                if (uiBase == null)
                {
                    Debug.LogError($"[UIManager] LoadUI - cannot find component, type({typeof(T)})");
                    return null;
                }

                UIResource uiResource = new UIResource();
                uiResource.Handle = handle;
                uiResource.UI = uiBase;

                _uiList.Add(inName, uiResource);
            }

            uiBase = _uiList[inName].UI as T;
            if (uiBase == null)
            {
                Debug.LogError($"[UIManager] LoadUI - cannot find component, type({typeof(T)})");
                return null;
            }

            uiBase.Initialize(inName, true);
            return uiBase;
        }

        public void ClearAll()
        {
            for (int i = _uiBaseList.Count -1; 0 <= i; i--)
            {
                if (_uiBaseList[i].DontDestroy == true)
                    continue;

                _uiBaseList.RemoveAt(i);
            }

            for (int i = _uiCurrentList.Count - 1; 0 <= i; i--)
            {
                if (_uiCurrentList[i].DontDestroy == true)
                    continue;

                _uiCurrentList.RemoveAt(i);
            }

            List<UIResource> uiList = _uiList.Values.ToList();
            for (int i = 0; i < uiList.Count; i++)
            {
                if (uiList[i] == null)
                    continue;

                if(uiList[i].UI != null)
                {
                    if (uiList[i].UI!.DontDestroy == true)
                        continue;

                    _uiList.Remove(uiList[i].UI!.Name);

                    uiList[i].UI!.transform.SetParent(null);
                }

                uiList[i].UI = null;
                Addressables.ReleaseInstance(uiList[i].Handle);
            }

            //RGM.Instance.EffectMgr.ClearAllEffect();
        }

        private Transform? GetLayer(Layer inLayer)
        {
            if (inLayer == Layer.Top)
                return _topRoot;
            else if (inLayer == Layer.Main)
                return _mainCanvas;
            else if (inLayer == Layer.Popup)
                return _popupRoot;
            else if (inLayer == Layer.Tooltip)
                return _popupRoot;
            else if (inLayer == Layer.MainBase)
                return _mainBase;

            return null;
        }

        private UIBase? FindEmptySpace(Vector3 mousePosition)
        {
            _pointerEventData.pointerId = -1;
            _pointerEventData.position = mousePosition;

            var results = new List<RaycastResult>();
            _topLayerRaycaster.Raycast(_pointerEventData, results);

            if (results.Count <= 0) // Top Layer에 아무것도 클릭된 UI가 없다면 Main Layer 체크
            {
                _mainLayerRaycaster.Raycast(_pointerEventData, results);
            }

            if (results.Count <= 0)
                return null;

            if (results[0].gameObject.CompareTag("UIEmptySpace") == false)
                return null;

            UIBase uiBase = results[0].gameObject.GetComponentInParent<UIBase>();
            return uiBase;
        }

        public bool IsPointerOverUIElement(Vector3 mousePosition)
        {
            _pointerEventData.position = mousePosition;
            List<RaycastResult> results = new();

            _topLayerRaycaster.Raycast(_pointerEventData, results);
            if (results.Count > 0)
            {
                return true;
            }
            _mainLayerRaycaster.Raycast(_pointerEventData, results);
            if (results.Count > 0)
            {
                return true;
            }

            return false;
        }

        public void UpdateLocalization()
        {
            //int index = DataManager.Instance.LocalizationIndex;
            //for (int i = 0; i < _uiCurrentList.Count; i++)
            //{
            //    _uiCurrentList[i].UpdateLocalization(index);
            //}
            //OnLangugeChanged?.Invoke();
        }

        private void Update()
        {
            UpdateInput();
        }

        private void UpdateInput()
        {
            if (_isLocked == true)
                return;

            bool isAllPass = true;
            for (int i = _uiCurrentList.Count - 1; 0 <= i; i--) // 활성화된 UI Input 처리
            {
                if (_uiCurrentList[i].UpdateInput(_input) == true)
                {
                    isAllPass = false;
                    break;
                }
            }

            if(isAllPass == true) // Base UI Input 처리
            {
                for (int i = _uiBaseList.Count - 1; 0 <= i; i--)
                {
                    if (_uiBaseList[i].UpdateInput(_input) == true)
                    {
                        isAllPass = false;
                        break;
                    }
                }
            }
            
            if(isAllPass == true) // Player Input 처리
            {
                if (_input.UI.RightClick.WasReleasedThisFrame() == true) // 
                {
                    // RobotController? robot = Hub.s?.pdata?.OwnerRobot;
                    // if (robot == null)
                    //     return;
                    //
                    // Shared.Item.ItemInstance? holdingItem = robot.Toolbelt?.HoldingItemInstance;
                    // if (holdingItem != null && holdingItem.ConsumableComponent != null && holdingItem.IsRobotItem == true)
                    // {
                    //     robot.ConsumeItemRpc(robot.Toolbelt!.HoldingItemInventorySlotIndex);
                    // }
                }
                else if(_input.Player.UseItem.WasReleasedThisFrame() == true)
                {
                    if (Hub.s?.pdata?.OwnerPlayer == null)
                        return;

                    Shared.Item.ItemInstance? holdingItem = Hub.s.pdata.OwnerPlayer?.Toolbelt?.HoldingItemInstance;
                    if(holdingItem != null && holdingItem.ConsumableComponent != null) 
                    {
                        Hub.s.pdata.OwnerPlayer!.UseItem(holdingItem.ItemInstanceId);
                    }
                }
                else if (_input.Player.ShowMenu.WasReleasedThisFrame())
                {
                    if (Hub.isUnderGround == false)
                        return;

                    Show<UIPrefab_Menu>(AddressablePath.UIPrefab_Menu, Layer.Top);
                }
            }
        }

        private void ReleaseDummySlot()
        {
            //if (_dummySlot.Index != -1)
            //{
            //    // 모두 갱신
            //    _dummySlot.UpdateSlots();
            //}
            //_isDragging = false;
            //_dummySlot.SetDummySlot(null, -1);
            //_dummySlot.gameObject.SetActive(false);
            //_dummySlot.ParentHandler = null;
        }

        // base를 뺀 모든 UI 닫기
        public void CloseNonBaseUI()
        {
            for (int i = _uiCurrentList.Count - 1; i >= 0; i--)
            {
                _uiCurrentList[i].Close();
            }
        }

        public void CloseCurrentUI()
        {
            if (0 < _uiCurrentList.Count)
            {
                int lastIndex = _uiCurrentList.Count - 1;
                _uiCurrentList[lastIndex].Close();
            }
        }

        public void SetHeldItemSlots(InventoryComponent heldInventory)
        {
            _dummySlot.SetSlot(heldInventory);
            var discardSlot = Get<InventoryBaseUI>(AddressablePath.UIPrefab_Inventory)?.DiscardSlot;
            if(discardSlot != null) 
            {
                discardSlot.SetSlot(heldInventory);
            }
        }

        public void HandleItemQuickStack(SlotUI clickedSlot)
        {
            if (_dummySlot.HasItem())
            {
                _dummySlot.TargetInventory.ChangeItemSlotServerRpc(0, clickedSlot.ParentHandler.GetNetworkObjectReference(), (int)clickedSlot.ParentHandler.GetNetworkBehaviourId(), clickedSlot.Index);
            }
            else
            {
                if (Hub.s!.uiman.IsShow(AddressablePath.UIPrefab_UnderGroundStorage))
                {
                    if (clickedSlot.CurrentItem == null)
                    {
                        return;
                    }

                    ISlotHandler originHandler = clickedSlot.ParentHandler;
                    
                    var mainInventory = Hub.s.uiman.Get<InventoryBaseUI>(AddressablePath.UIPrefab_Inventory);
                    var toolbeltInventory = Hub.s.uiman.Get<InventoryBaseUI>(AddressablePath.UIPrefab_Toolbelt);
                    var storageInventory = Hub.s.uiman.Get<InventoryBaseUI>(AddressablePath.UIPrefab_UnderGroundStorage);

                    if ((object)originHandler == mainInventory || (object)originHandler == toolbeltInventory)
                    {
                        originHandler.PerformQuickStackOrMove(clickedSlot.Index, storageInventory.TargetInventory);
                    }
                    else if ((object)originHandler == storageInventory)
                    {
                        originHandler.PerformQuickStackOrMove(clickedSlot.Index, mainInventory.TargetInventory);
                    }
                    else
                    {
                        Debug.LogWarningFormat("[UIManager] HandleItemQuickStack - originHandler is not found, {0}", originHandler.GetType().Name);
                    }
                }
            }
        }

        /// <summary>
        /// 슬롯 클릭 시 처리를 담당합니다. 
        /// </summary>
        /// <param name="clickedSlot">클릭한 슬롯.</param>
        /// <param name="quantity">옮길 수량.</param>
        /// @note: -1은 전체 수량을 의미합니다.
        public void HandleSlotClick(SlotUI clickedSlot, int quantity = -1)
        {
            bool isDivide = quantity > 0;

            var dummy = _dummySlot;
            var dummyItem = dummy.CurrentItem;
            bool isDiscardSlot = clickedSlot.Type == SlotUI.SlotType.Discard;
            var discardSlot = Hub.s!.uiman.Get<InventoryBaseUI>(AddressablePath.UIPrefab_Inventory)?.DiscardSlot;


            // 1. 빈 클릭, 더미도 비었으면 return
            if (clickedSlot.CurrentItem == null && !dummy.HasItem() && !discardSlot.HasItem())
                return;

            // 2. 첫 클릭 (pick up)
            if (!dummy.HasItem())
            {
                if (isDivide)
                {
                    // right-click: pick partial
                    if (isDiscardSlot)
                    {
                        discardSlot.TargetInventory.DivideItem(clickedSlot.Index, quantity, dummy.TargetInventory, dummy.Index);
                    }
                    else
                    {
                        clickedSlot.ParentHandler.DivideItem(clickedSlot.Index, quantity, dummy.TargetInventory, dummy.Index);
                    }
                }
                else
                {
                    // left-click: pick all
                    if (clickedSlot.EquipmentType != string.Empty)
                    {
                        if(clickedSlot.Type == SlotUI.SlotType.Equipment)
                        {
                            clickedSlot.UnEquipItem(clickedSlot.EquipmentType, dummy.TargetInventory, 0);
                        }
                        else if (clickedSlot.Type == SlotUI.SlotType.FurnaceMaterial)
                        {
                            clickedSlot.ParentHandler.DragAndDrop(clickedSlot, _dummySlot);
                        }
                        else if (clickedSlot.Type == SlotUI.SlotType.FurnaceResult)
                        {
                            clickedSlot.ParentHandler.DragAndDrop(clickedSlot, _dummySlot);
                        }
                        else if (clickedSlot.Type == SlotUI.SlotType.AntenaSlot)
                        {
                            clickedSlot.ChangeItemSlot(clickedSlot.Index, dummy.TargetInventory.NetworkObject, dummy.Index);
                        }
                    }
                    else if (isDiscardSlot)
                    {
                        discardSlot.TargetInventory.ChangeItemSlotServerRpc(clickedSlot.Index, dummy.TargetInventory.NetworkObject, dummy.Index);
                    }
                    else
                    {
                        clickedSlot.ChangeItemSlot(clickedSlot.Index, dummy.TargetInventory.NetworkObject, dummy.Index);
                    }
                }
                return;
            }

            // 3. 드랍 (dummy has item)
            if(clickedSlot.SlotEnabled == true) // 슬롯이 활성화 되어 있을때만 드랍이 가능하다.
            {
                bool isCustomSlot = clickedSlot.EquipmentType != string.Empty;
                var clickedItem = clickedSlot.CurrentItem;
                if (isDiscardSlot)
                {
                    clickedItem = discardSlot.GetCurrentItem();
                }
                
                if (clickedSlot.Type == SlotUI.SlotType.Crafter)
                {
                    if (dummyItem.ItemInfo.CannotBeCooked)
                    {
                        Show<UIPrefab_Alarm>(AddressablePath.UIPrefab_Alarm, Layer.Popup)?.ShowAlarm("invalid_cook_item");
                        return;
                    }
                }

                // case: 빈 슬롯에 드랍
                if (clickedItem == null)
                {
                    if (isCustomSlot)
                    {
                        if (dummyItem.ItemInfo.EquipmentComponent != null &&
                            dummyItem.ItemInfo.EquipmentComponent.EquipmentSlot.Contains(clickedSlot.EquipmentType))
                        {
                            clickedSlot.EquipItem(clickedSlot.EquipmentType, dummy.TargetInventory, 0);
                        }
                        else
                        {
                            if (clickedSlot.Type == SlotUI.SlotType.FurnaceMaterial)
                            {
                                clickedSlot.ParentHandler.DragAndDrop(_dummySlot, clickedSlot);
                            }
                            else if (clickedSlot.Type == SlotUI.SlotType.FurnaceFuel)
                            {
                                clickedSlot.ParentHandler.DragAndDrop(_dummySlot, clickedSlot);
                            }
                            else if (clickedSlot.Type == SlotUI.SlotType.AntenaSlot) // 안테나 슬롯엔 아이템이 1개만 들어갈 수 있다.
                            {
                                dummy.TargetInventory.DivideItemServerRpc(dummy.Index, 1, clickedSlot.ParentHandler.GetNetworkBehaviourReference(), clickedSlot.Index);
                            }
                            else if (clickedSlot.Type == SlotUI.SlotType.Merchant)
                            {

                            }
                        }
                    }
                    else if (isDiscardSlot)
                    {
                        dummy.TargetInventory.ChangeItemSlotServerRpc(
                            0,
                            discardSlot.TargetInventory.NetworkObject,
                            discardSlot.TargetInventory.NetworkBehaviourId,
                            clickedSlot.Index);
                    }
                    else
                    {
                        dummy.TargetInventory.ChangeItemSlotServerRpc(
                            0,
                            clickedSlot.ParentHandler.GetNetworkObjectReference(),
                            (int)clickedSlot.ParentHandler.GetNetworkBehaviourId(),
                            clickedSlot.Index);
                    }
                    return;
                }

                if (clickedSlot.Type == SlotUI.SlotType.AntenaSlot) // 빈 슬롯이 아니라면 안테나 슬롯에는 아이템을 넣을 수 없다.
                {
                    return;
                }
                else
                {
                    // case: 스택 가능한 아이템 
                    if (dummyItem.ItemMasterId == clickedItem.ItemMasterId && clickedItem.CanBeStacked)
                    {
                        if (isDivide)
                        {
                            if (isDiscardSlot)
                            {
                                discardSlot.TargetInventory.DivideItem(clickedSlot.Index, quantity, dummy.TargetInventory, dummy.Index);
                            }
                            else
                            {
                                clickedSlot.ParentHandler.DivideItem(clickedSlot.Index, quantity, dummy.TargetInventory, dummy.Index);
                            }
                        }
                        else
                        {
                            if (isDiscardSlot)
                            {
                                discardSlot.TargetInventory.StackItemServerRpc(
                                    dummy.Index,
                                    discardSlot.TargetInventory,
                                    clickedSlot.Index);
                            }
                            else
                            {
                                dummy.TargetInventory.StackItemServerRpc(
                                    dummy.Index,
                                    clickedSlot.ParentHandler.GetNetworkBehaviourReference(),
                                    clickedSlot.Index);
                            }
                        }
                        return;
                    }

                    // case: 드랍 불가 → 스왑
                    if (isCustomSlot)
                    {
                        if (dummyItem.ItemInfo.EquipmentComponent != null &&
                            dummyItem.ItemInfo.EquipmentComponent.EquipmentSlot.Contains(clickedSlot.EquipmentType))
                        {
                            clickedSlot.UnEquipItem(clickedSlot.EquipmentType, dummy.TargetInventory, 0);
                            clickedSlot.EquipItem(clickedSlot.EquipmentType, dummy.TargetInventory, 0);
                        }
                        else
                        {
                            if (clickedSlot.Type == SlotUI.SlotType.FurnaceMaterial)
                            {
                                clickedSlot.ParentHandler.DragAndDrop(_dummySlot, clickedSlot);
                            }
                        }
                    }
                    else if (isDiscardSlot)
                    {
                        dummy.TargetInventory.ChangeItemSlotServerRpc(
                            dummy.Index,
                            discardSlot.TargetInventory.NetworkObject,
                            discardSlot.TargetInventory.NetworkBehaviourId,
                            clickedSlot.Index);
                    }
                    else
                    {
                        dummy.TargetInventory.ChangeItemSlotServerRpc(
                            0,
                            clickedSlot.ParentHandler.GetNetworkObjectReference(),
                            (int)clickedSlot.ParentHandler.GetNetworkBehaviourId(),
                            clickedSlot.Index);
                    }
                }
            }
        }

        private IEnumerator ShowTooltipCo(Vector3 inScreenPosition, Vector3 inScreenLeftPosition)
        {
            yield return null;

            Vector2 pos = GetPositionTooltip(inScreenPosition, inScreenLeftPosition);
            _tooltip!.SetPosition(pos);
        }

        private void StopShowSaveText()
        {
            if (_saveCoroutine != null)
            {
                StopCoroutine(_saveCoroutine);
            }

            _saveCoroutine = null;
            _loadingText.gameObject.SetActive(false);
        }

        private IEnumerator ShowSaveText()
        {
            _loadingText.text = "Save Complete...";
            _loadingText.gameObject.SetActive(true);
            _loadingText.color = new Color(1f, 1f, 1f, 0f);

            float fadeInSpeed = 1f / 0.7f;
            float fadeOutSpeed = 1f / 0.3f;

            for (float t = 0; t <= 0.7f; t += Time.deltaTime)
            {
                _loadingText.color = new Color(1f, 1f, 1f, t * fadeInSpeed);
                yield return null;
            }
            _loadingText.color = Color.white;

            for (float t = 0; t < 0.3f; t += Time.deltaTime)
            {
                _loadingText.color = new Color(1f, 1f, 1f, 1f - t * fadeOutSpeed);
                yield return null;
            }
            _loadingText.gameObject.SetActive(false);
        }

        // hACK;
        private void OnDestroy()
        {
            DisableInputSystem();
        }

        //private T LoadTooltip<T>(string inName) where T : UITooltip
        //{
        //    T tooltip = null;
        //    if (_uiList.ContainsKey(inName) == true)
        //    {
        //        tooltip = _uiList[inName] as T;
        //    }
        //    else
        //    {
        //        tooltip = LoadUI<T>(inName, Layer.Main);
        //    }

        //    if (_uiTooltipList.Contains(tooltip) == false)
        //    {
        //        _uiTooltipList.Add(tooltip);
        //    }

        //    return tooltip;
        //}
    }
}

*/
