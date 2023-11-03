using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

[RequireComponent(typeof(TMP_Dropdown))]
public class UI_SettingDropdown : DIMono
{
    [DoNotSerialize]
    [Inject] private SettingData settingData;
    [Inject] private Settings _settings;
    [SerializeField] private SettingValueList settingValueList;
    [SerializeField] private TMP_Text settingNameText;

    private SubscribeObject _subscribeObject = new();

    public enum SettingValueList
    {
        Resolution,
        FrameRate,
        FullScreenMode,
        Localization
    }

    private void OnDestroy() => _subscribeObject.UnsubscribeAll();

    private int GetValue() => settingValueList switch
    {
        SettingValueList.Resolution     => settingData.graphic.resolutionIndex,
        SettingValueList.FrameRate      => settingData.graphic.frameRate,
        SettingValueList.FullScreenMode => (int)settingData.graphic.fullScreenMode,
        SettingValueList.Localization   => settingData.general.LocalizationIndex,
        _                               => throw new ArgumentOutOfRangeException()
    };

    public void SetValue(int _value)
    {
        switch (settingValueList)
        {
            case SettingValueList.Resolution:
                settingData.graphic.resolutionIndex = _value;
                SettingData.Resolution _resolution = SettingData.Graphic.ResolutionList[_value];
                _resolution.ApplyResolution(settingData);

                if (_settings == null)
                    break;
                _settings.SetResolution(_resolution.width, _resolution.height);
                break;
            case SettingValueList.FrameRate:
                settingData.graphic.frameRate = _value;
                Application.targetFrameRate   = Settings.GetRefreshRateByIndex(_value);

                Debug.Log($"FrameRate: {Application.targetFrameRate}");
                break;
            case SettingValueList.FullScreenMode:
                settingData.graphic.fullScreenMode = (FullScreenMode)(_value > 1 ? 3 : _value);
                Screen.fullScreenMode              = settingData.graphic.fullScreenMode;
                break;
            case SettingValueList.Localization:
                settingData.general.LocalizationIndex = _value;
                LocalizationSettings.SelectedLocale   = LocalizationSettings.AvailableLocales.Locales[_value];
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public override void Initialize()
    {
        settingNameText.text = settingValueList.ToString();

        LocalizeStringEvent _localizeStringEvent = settingNameText.GetComponent<LocalizeStringEvent>();
        _localizeStringEvent.SetTable($"Settings");
        _localizeStringEvent.SetEntry($"{settingValueList}");

        Dictionary<string, string> _optionLocalizeTable = new();
        AddLocalizeDict("Unlimited");
        AddLocalizeDict("Exclusive Fullscreen");
        AddLocalizeDict("Fullscreen Window");
        AddLocalizeDict("Windowed");

        TMP_Dropdown _dropdown = GetComponent<TMP_Dropdown>();
        _dropdown.onValueChanged.AddListener(SetValue);
        _dropdown.ClearOptions();
        InitializeDefaultSetting();

        _dropdown.value = GetValue();

        switch (settingValueList)
        {
            case SettingValueList.FrameRate:
                LocalizationSettings.SelectedLocaleChanged += _ =>
                {
                    _optionLocalizeTable.Clear();
                    AddLocalizeDict("Unlimited");
                    _dropdown.options[^1].text = _optionLocalizeTable["Unlimited"];
                };

                // _subscribeObject.Subscribe<LocalizationChangedEvent>(_ =>
                // {
                //     _optionLocalizeTable.Clear();
                //     AddLocalizeDict("Unlimited");
                //     _dropdown.options[^1].text = _optionLocalizeTable["Unlimited"];
                // });

                break;

            case SettingValueList.FullScreenMode:
                LocalizationSettings.SelectedLocaleChanged += _ =>
                {
                    _optionLocalizeTable.Clear();
                    AddLocalizeDict("Unlimited");
                    AddLocalizeDict("Exclusive Fullscreen");
                    AddLocalizeDict("Fullscreen Window");
                    AddLocalizeDict("Windowed");
                    _dropdown.options[0].text = _optionLocalizeTable["Exclusive Fullscreen"];
                    _dropdown.options[1].text = _optionLocalizeTable["Fullscreen Window"];
                    _dropdown.options[2].text = _optionLocalizeTable["Windowed"];

                    _dropdown.captionText.text = _dropdown.options[_dropdown.value].text;
                };

                // _subscribeObject.Subscribe<LocalizationChangedEvent>(_ =>
                // {
                //     _optionLocalizeTable.Clear();
                //     AddLocalizeDict("Unlimited");
                //     AddLocalizeDict("Exclusive Fullscreen");
                //     AddLocalizeDict("Fullscreen Window");
                //     AddLocalizeDict("Windowed");
                //     _dropdown.options[0].text = _optionLocalizeTable["Exclusive Fullscreen"];
                //     _dropdown.options[1].text = _optionLocalizeTable["Fullscreen Window"];
                //     _dropdown.options[2].text = _optionLocalizeTable["Windowed"];
                //
                //     _dropdown.captionText.text = _dropdown.options[_dropdown.value].text;
                // });
                break;

            default:
                break;
        }

        return;

        void AddLocalizeDict(string _key) =>
            _optionLocalizeTable.Add(_key, LocalizationSettings.StringDatabase.GetLocalizedString("Settings", $"{_key}"));

        void InitializeDefaultSetting()
        {
            switch (settingValueList)
            {
                case SettingValueList.Resolution:
                    _dropdown.AddOptions(Screen.resolutions.Select(_res => $"{_res.width}x{_res.height}").Distinct().ToList());
                    break;
                case SettingValueList.FrameRate:
                    _dropdown.AddOptions(new List<string>
                                         {
                                             "30",
                                             "60",
                                             "75",
                                             "120",
                                             "144",
                                             "180",
                                             "240",
                                             "300",
                                             _optionLocalizeTable["Unlimited"],
                                         });
                    _dropdown.options[^1].text = _optionLocalizeTable["Unlimited"];
                    break;
                case SettingValueList.FullScreenMode:
                    _dropdown.AddOptions(new List<string>
                                         {
                                             _optionLocalizeTable["Exclusive Fullscreen"],
                                             _optionLocalizeTable["Fullscreen Window"],
                                             _optionLocalizeTable["Windowed"],
                                         });
                    break;
                case SettingValueList.Localization:
                    List<string> _tmpList = new() { null };
                    foreach (Locale _availableLocalesLocale in LocalizationSettings.AvailableLocales.Locales)
                    {
                        _tmpList[0] = _availableLocalesLocale.LocaleName;
                        _dropdown.AddOptions(_tmpList);
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}