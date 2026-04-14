import pywhatkit
import time
import pyautogui
from pynput.keyboard import Key, Controller

def send_continuous_messages(phone_no, message, count):
    """
    Sends a specified message continuously for a given count.
    Note: Requires manual intervention to open the chat window after the first message setup.
    """
    keyboard = Controller()
    try:
        # Initial message setup with instant function
        # This opens the browser and types the message, but might not press 'Enter' automatically
        pywhatkit.sendwhatmsg_instantly(phone_no, message, tab_close=True)
        
        # Give time for the page to load
        time.sleep(10) 
        
        # Use pyautogui to click and ensure the window is active
        pyautogui.click() 
        time.sleep(2)
        
        # Send subsequent messages using a loop
        for _ in range(count - 1): # -1 because the first one was "sent" (typed)
            pyautogui.typewrite(message)
            keyboard.press(Key.enter)
            keyboard.release(Key.enter)
            time.sleep(1) # Add a small delay between messages

        print("Messages sent!")

    except Exception as e:
        print(f"An error occurred: {str(e)}")
        print("Make sure you are logged into WhatsApp Web and a stable internet connection is present.")

# --- Usage Example ---
# Replace with the recipient's phone number, including the country code (e.g., '+1234567890')
recipient_number = "+1234567890" 
# The message content
message_content = "This is a continuous test message!"
# The number of messages to send
message_count = 5 

send_continuous_messages(recipient_number, message_content, message_count)

