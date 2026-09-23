using UnityEngine;
using UnityEngine.InputSystem;

namespace Paintball.Unity.Input
{
    /// <summary>
    /// Brücke zwischen Unity Input System und PlayerController (AR-05).
    /// Ermöglicht automatische Erkennung und Umschaltung des aktiven Eingabegeräts (UX-11).
    /// Unterstützt Maus/Tastatur, Touch-Virtual-Joystick und Gamepad (UX-08, UX-09, UX-10).
    /// </summary>
    public sealed class PlayerInputBridge : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Player.PlayerController _playerController;
        [SerializeField] private Camera.PlayerCameraController _cameraController;

        [Header("Settings")]
        [SerializeField] private bool _enableGyroAim;

        private Vector2 _lookAccumulator;
        private bool _fireHeld;
        private bool _sprintHeld;
        private bool _crouchHeld;

        private enum InputDeviceType { MouseKeyboard, Gamepad, Touch }
        private InputDeviceType _currentDeviceType = InputDeviceType.MouseKeyboard;

        public InputDeviceType CurrentInputDevice => _currentDeviceType;

        private void OnEnable()
        {
            if (Keyboard.current != null)
                Keyboard.current.onTextInput += OnTextInput;
        }

        private void OnDisable()
        {
            if (Keyboard.current != null)
                Keyboard.current.onTextInput -= OnTextInput;
        }

        private void OnTextInput(char ch) { }

        private void Update()
        {
            DetectDeviceType();
            PollMovement();
            PollLook();
            PollActions();
        }

        private void DetectDeviceType()
        {
            if (Gamepad.current != null && Gamepad.current.allControls.Count > 0)
            {
                bool gamepadActive = Gamepad.current.leftStick.ReadValue().sqrMagnitude > 0.1f ||
                                     Gamepad.current.rightStick.ReadValue().sqrMagnitude > 0.1f;
                if (gamepadActive) _currentDeviceType = InputDeviceType.Gamepad;
            }

            if (Mouse.current != null && (Mouse.current.delta.ReadValue().sqrMagnitude > 0.1f || Mouse.current.leftButton.isPressed))
                _currentDeviceType = InputDeviceType.MouseKeyboard;
        }

        private void PollMovement()
        {
            Vector2 move = Vector2.zero;

            if (Keyboard.current != null)
            {
                bool w = Keyboard.current.wKey.isPressed;
                bool s = Keyboard.current.sKey.isPressed;
                bool a = Keyboard.current.aKey.isPressed;
                bool d = Keyboard.current.dKey.isPressed;
                move = new Vector2((d ? 1 : 0) - (a ? 1 : 0), (w ? 1 : 0) - (s ? 1 : 0));
            }

            if (Gamepad.current != null)
            {
                Vector2 stickMove = Gamepad.current.leftStick.ReadValue();
                if (stickMove.sqrMagnitude > move.sqrMagnitude)
                    move = stickMove;
            }

            if (Touchscreen.current != null && Touchscreen.current.touches.Count > 0)
            {
                var touch = Touchscreen.current.touches[0];
                if (touch.isInProgress)
                {
                    Vector2 touchDelta = touch.delta.ReadValue();
                    move = touchDelta / (Screen.width * 0.1f);
                }
            }

            if (_playerController != null)
                _playerController.MoveInput = Vector2.ClampMagnitude(move, 1f);
        }

        private void PollLook()
        {
            Vector2 look = Vector2.zero;

            if (Mouse.current != null && _currentDeviceType == InputDeviceType.MouseKeyboard)
            {
                look = Mouse.current.delta.ReadValue();
            }

            if (Gamepad.current != null && _currentDeviceType == InputDeviceType.Gamepad)
            {
                look = Gamepad.current.rightStick.ReadValue() * 120f;
            }

            if (_enableGyroAim && SystemInfo.supportsGyroscope)
            {
                var gyro = UnityEngine.InputSystem.Gyroscope.current;
                if (gyro != null && gyro.enabled)
                {
                    Vector3 gyroRate = gyro.angularVelocity.ReadValue();
                    look += new Vector2(gyroRate.y, gyroRate.x) * Mathf.Rad2Deg * 0.5f;
                }
            }

            if (_cameraController != null)
                _cameraController.AddRotation(look.x, look.y);

            if (_playerController != null)
                _playerController.RotatePlayer(look.x * 0.01f);
        }

        private void PollActions()
        {
            bool fire = false;
            bool jump = false;
            bool reload = false;

            if (Keyboard.current != null)
            {
                fire = Mouse.current != null && Mouse.current.leftButton.isPressed;
                jump = Keyboard.current.spaceKey.wasPressedThisFrame;
                reload = Keyboard.current.rKey.wasPressedThisFrame;
                _sprintHeld = Keyboard.current.leftShiftKey.isPressed;
                _crouchHeld = Keyboard.current.cKey.isPressed;
            }

            if (Gamepad.current != null)
            {
                fire = fire || Gamepad.current.rightTrigger.isPressed;
                jump = jump || Gamepad.current.aButton.wasPressedThisFrame;
                reload = reload || Gamepad.current.xButton.wasPressedThisFrame;
                _sprintHeld = _sprintHeld || Gamepad.current.leftStickButton.isPressed;
                _crouchHeld = _crouchHeld || Gamepad.current.bButton.isPressed;
            }

            if (_playerController != null)
            {
                _playerController.FireHeld = fire;
                _playerController.JumpPressed = jump;
                _playerController.SprintHeld = _sprintHeld;
                _playerController.CrouchHeld = _crouchHeld;
            }
        }
    }
}
