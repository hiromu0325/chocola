using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace StarterAssets
{
	/// <summary>
	/// 入力の集約。PlayerInput のコントロールスキーム切替に依存せず、
	/// キーボード／マウス／ゲームパッドを毎フレーム直接読む（接続状況に左右されない）。
	/// </summary>
	public class StarterAssetsInputs : MonoBehaviour
	{
		[Header("Character Input Values")]
		public Vector2 move;
		public Vector2 look;
		public bool jump;
		public bool sprint;
		[Header("Interact")]
		public bool interact;

		[Header("Movement Settings")]
		public bool analogMovement;
		/// <summary>今フレームの視点入力がマウス由来か（FPCのdeltaTime処理用）</summary>
		[HideInInspector] public bool lookIsMouse = true;

		[Header("Mouse Cursor Settings")]
		public bool cursorLocked = true;
		public bool cursorInputForLook = true;

		[Header("Look Sensitivity / Invert")]
		[Tooltip("マウス感度（生のピクセル移動量に掛ける係数。小さいほどゆっくり）")]
		public float mouseSensitivity = 0.1f;
		[Tooltip("左右（ヨー）反転")]
		public bool invertLookX = false;
		[Tooltip("上下（ピッチ）反転")]
		public bool invertLookY = false;

		[Header("Gamepad")]
		[Tooltip("右スティックの視点速度")]
		public float gamepadLookSpeed = 150f;
		[Tooltip("スティックの遊び")]
		public float stickDeadzone = 0.15f;

#if ENABLE_INPUT_SYSTEM
		private void Update()
		{
			PollDevices();
		}

		private void PollDevices()
		{
			// ===== 移動（WASD / 矢印 / 左スティック / 十字キー）=====
			Vector2 m = Vector2.zero;
			var kb = Keyboard.current;
			if (kb != null)
			{
				if (kb.wKey.isPressed || kb.upArrowKey.isPressed) m.y += 1f;
				if (kb.sKey.isPressed || kb.downArrowKey.isPressed) m.y -= 1f;
				if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) m.x += 1f;
				if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) m.x -= 1f;
			}

			bool sprintHeld = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
			bool jumpHeld = kb != null && kb.spaceKey.isPressed;
			bool interactHeld = kb != null && kb.eKey.isPressed;

			bool usingStick = false;
			var gp = Gamepad.current;
			if (gp != null)
			{
				Vector2 ls = gp.leftStick.ReadValue();
				if (ls.magnitude > stickDeadzone && ls.sqrMagnitude > m.sqrMagnitude)
				{
					m = ls;
					usingStick = true;
				}
				if (gp.dpad.up.isPressed) m.y = 1f;
				if (gp.dpad.down.isPressed) m.y = -1f;
				if (gp.dpad.left.isPressed) m.x = -1f;
				if (gp.dpad.right.isPressed) m.x = 1f;

				sprintHeld |= gp.leftStickButton.isPressed || gp.leftTrigger.ReadValue() > 0.5f;
				jumpHeld |= gp.buttonSouth.isPressed;                       // A / ×
				interactHeld |= gp.buttonWest.isPressed || gp.rightTrigger.ReadValue() > 0.5f; // X / R2
			}

			analogMovement = usingStick;
			move = Vector2.ClampMagnitude(m, 1f);
			sprint = sprintHeld;
			jump = jumpHeld;
			interact = interactHeld;

			// ===== 視点（マウス delta / 右スティック）=====
			Vector2 lookVal = Vector2.zero;
			lookIsMouse = true;
			if (cursorInputForLook)
			{
				Vector2 mouseDelta = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
				if (mouseDelta.sqrMagnitude > 0.0001f)
				{
					lookVal = mouseDelta * mouseSensitivity;
					lookIsMouse = true;
				}
				else if (gp != null)
				{
					Vector2 rs = gp.rightStick.ReadValue();
					if (rs.magnitude > stickDeadzone)
					{
						lookVal = rs * gamepadLookSpeed;
						lookIsMouse = false;
					}
				}
			}
			// デフォルトで上下を反転（従来の向きの逆を既定とする）
			lookVal.y = -lookVal.y;
			if (invertLookX) lookVal.x = -lookVal.x;
			if (invertLookY) lookVal.y = -lookVal.y;
			look = lookVal;
		}

		// ---- 旧 PlayerInput メッセージ互換（使われなくても害なし）----
		public void OnMove(InputValue value) { }
		public void OnLook(InputValue value) { }
		public void OnJump(InputValue value) { }
		public void OnSprint(InputValue value) { }
		public void OnInteract(InputValue value) { }
#endif

		public void MoveInput(Vector2 newMoveDirection) { move = newMoveDirection; }
		public void LookInput(Vector2 newLookDirection) { look = newLookDirection; }
		public void JumpInput(bool newJumpState) { jump = newJumpState; }
		public void SprintInput(bool newSprintState) { sprint = newSprintState; }
		public void InteractInput(bool newInteractState) { interact = newInteractState; }

		private void OnApplicationFocus(bool hasFocus)
		{
			SetCursorState(cursorLocked);
		}

		private void SetCursorState(bool newState)
		{
			Cursor.lockState = newState ? CursorLockMode.Locked : CursorLockMode.None;
		}
	}
}
