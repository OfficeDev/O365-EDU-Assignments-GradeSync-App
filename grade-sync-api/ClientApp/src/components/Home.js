import React, {
    useContext,
    useEffect,
    useReducer,
    useState,
    Fragment } from 'react';
import { useNavigate } from 'react-router-dom';
import ApiService from '../services/ApiService';
import MappingModal from './MappingModal';
import { TeamsContext } from '../App';
import SyncStatus from '../helpers/SyncStatusEnum';
import JobStatus from '../helpers/JobStatusEnum';
import { Modal, ModalHeader, ModalBody, ModalFooter } from 'reactstrap';
import noTeams from '../img/teams_splash_graphic.svg';

import * as teamsjs from "@microsoft/teams-js";
const { authentication } = teamsjs;

const Home = (props) => {
    const navigate = useNavigate();
    const { tabContext } = useContext(TeamsContext);

    const [runningJobId, setRunningJobId] = useState("");
    const [jobStatus, setJobStatus] = useState(null);
    const [jobInterval, setJobInterval] = useState(null);
    const [cancelSyncInProgress, setCancelSyncInProgress] = useState(false);
    const [defaultConnectionId, setDefaultConnectionId] = useState(undefined);
    const [showNoDefaultConnectionWarning, setConnectionWarning] = useState(false);
    const [forceSync, setForceSync] = useState(false);
    
    // MappingModal states
    const [showMappingModal, setShowMappingModal] = useState(false);
    const [fetchingMappingState, setFetchingMappingState] = useState(false);

    const initState = {
        assignments: [],
        activeJobId: null,
        fetchedAssignments: false,
        syncAll: false,
        syncInProgress: false,
        assignmentSelectedCount: 0,
        oneRosterAssignmentCategories: [],
        assignmentCategoryMap: {},
        showMappingToolWarning: false,
        mappingToolDisabled: true,
        errorsModal: {
            show: false,
            assignment: {}
        },
        mappingModalState: {
            pages: ["accounts", "classes", "students"],
            activePage: "accounts",
            pageHasDataMap: {
                "accounts": false,
                "classes": false,
                "students": false
            }
        }
    }

    const categoriesToMap = (categories) => {
        const catMap = {};
        for (const cat of categories) {
            const modified = cat.title.replaceAll(' ', '').toLowerCase();
            catMap[modified] = cat.id;
        }

        return catMap;
    }

    const categoryMatchId = (graphAssignmentCat, modifiedCategoryMap) => {
        const modified = graphAssignmentCat.replaceAll(' ', '').toLowerCase();
        if (modifiedCategoryMap.hasOwnProperty(modified)) {
            return modifiedCategoryMap[modified];
        } else return null;
    }

    const stateReducer = (state, action) => {
        const stateCopy = {...state};

        if (action.type === "setAssignments") {
            stateCopy.assignments = action.assignmentList;
            console.log(stateCopy.assignments);

            stateCopy.activeJobId = null;
            stateCopy.syncAll = false;
            stateCopy.assignmentSelectedCount = 0;
            let syncInProgress = false;
            let includesCategories = false;
            let modifiedCatMap;

            if (action.categories && action.categories.length > 0) {
                stateCopy.oneRosterAssignmentCategories = action.categories;
                includesCategories = true;
                modifiedCatMap = categoriesToMap(stateCopy.oneRosterAssignmentCategories);
            }

            for (const assignment of stateCopy.assignments) {
                // check for currently running job
                if (assignment.syncStatus === SyncStatus.InProgress) {
                    if (action.polledJobFinished) {
                        // this would be a rare case where the job failed during setup of additional job data, and also had an error
                        // fetching the assignment entities to sync, so it wouldn't be able to update the job status to failed.

                        // this way it will at least allow you to queue another job
                        assignment.syncStatus = SyncStatus.Failed;
                    } else {
                        stateCopy.activeJobId = assignment.currentSyncJobId;
                        syncInProgress = true;
                    }
                }

                // check for categories on assignment
                if (includesCategories) {
                    if (assignment.stringifiedCategoryDict) {
                        // parsed = {connectionId: {catId: string, lineItemSynced: bool}}
                        const parsed = JSON.parse(assignment.stringifiedCategoryDict);

                        if (parsed.hasOwnProperty(defaultConnectionId)) {
                            // if this assignment has been synced to OneRoster, we no longer allow to change the category
                            // since we do not overwrite lineItems
                            assignment.canSetCategory = !parsed[defaultConnectionId]["lineItemSynced"];
                            stateCopy.assignmentCategoryMap[assignment.assignmentId] = parsed[defaultConnectionId]["catId"];
                        } else {
                            assignment.canSetCategory = true;
                            if (assignment.graphGradingCategoryName) {
                                // attempt to text match graph grading category to possible OneRoster category
                                const matchId = categoryMatchId(assignment.graphGradingCategoryName, modifiedCatMap);
                                if (matchId) stateCopy.assignmentCategoryMap[assignment.assignmentId] = matchId;
                            }
                        }
                    } else {
                        assignment.canSetCategory = true;
                        if (assignment.graphGradingCategoryName) {
                            // attempt to text match graph grading category to possible OneRoster category
                            const matchId = categoryMatchId(assignment.graphGradingCategoryName, modifiedCatMap);
                            if (matchId) stateCopy.assignmentCategoryMap[assignment.assignmentId] = matchId;
                            assignment.canSetCategory = true;
                        }
                    }
                }
            }

            stateCopy.syncInProgress = syncInProgress;
            stateCopy.fetchedAssignments = true;
        }

        if (action.type === "setMappingToolState") {
            stateCopy.mappingToolDisabled = action.mappingState.mappingToolDisabled;

            if (!stateCopy.mappingToolDisabled) {
                stateCopy.mappingModalState.pageHasDataMap["accounts"] = action.mappingState.hasAccountExternalId;
                stateCopy.mappingModalState.pageHasDataMap["classes"] = action.mappingState.hasClassExternalId;
                stateCopy.mappingModalState.pageHasDataMap["students"] = action.mappingState.hasAllStudentExternalIds;

                let showWarning = false;
                for (const page of stateCopy.mappingModalState.pages) {
                    if (stateCopy.mappingModalState.pageHasDataMap[page] === false) {
                        stateCopy.mappingModalState.activePage = page;
                        showWarning = true;
                        break;
                    }
                }

                stateCopy.showMappingToolWarning = showWarning;
            }
        }

        if (action.type === "markMappingPageDataTrue") {
            stateCopy.mappingModalState.pageHasDataMap[action.page] = true;

            let showWarning = false;
            for (const page of stateCopy.mappingModalState.pages) {
                if (stateCopy.mappingModalState.pageHasDataMap[page] === false) {
                    stateCopy.mappingModalState.activePage = page;
                    showWarning = true;
                    break;
                }
            }
            stateCopy.showMappingToolWarning = showWarning;
        }

        if (action.type === "changeMappingToolPage") {
            stateCopy.mappingModalState.activePage = action.page;
        }

        if (action.type === "queueSync") {
            stateCopy.assignmentSelectedCount += 1;
            for (const val of stateCopy.assignments) {
                if (val.assignmentId === action.assignment.assignmentId) {
                    val.queuedToSync = true;
                    break;
                }
            }
        }

        if (action.type === "dequeueSync") {
            stateCopy.assignmentSelectedCount -= 1;
            for (const val of stateCopy.assignments) {
                if (val.assignmentId === action.assignment.assignmentId) {
                    val.queuedToSync = false;
                    break;
                }
            }
        }

        if (action.type === "syncAll") {
            stateCopy.syncAll = action.value;
            
            let selected = 0;
            for (const assignment of stateCopy.assignments) {
                if (stateCopy.syncAll) {
                    if (assignment.status !== "draft" && assignment.maxPoints) {
                        assignment.queuedToSync = true;
                        selected += 1;
                    }
                } else {
                    assignment.queuedToSync = false;
                } 
            }

            if (stateCopy.syncAll) {
                stateCopy.assignmentSelectedCount = selected;
            } else {
                stateCopy.assignmentSelectedCount = 0;
            }
        }

        if (action.type === "quickChangeSyncStatus") {
            stateCopy.syncInProgress = true;

            for (const assignment of stateCopy.assignments) {
                if (assignment.queuedToSync) {
                    assignment.syncStatus = SyncStatus.InProgress;
                }
            }
        }

        if (action.type === "showErrorsModal") {
            stateCopy.errorsModal.show = true;
            stateCopy.errorsModal.assignment = action.assignment;
        }

        if (action.type === "hideErrorsModal") {
            stateCopy.errorsModal.show = false;
            stateCopy.errorsModal.assignment = {};
        }

        if (action.type === "clearActiveJobState") {
            stateCopy.activeJobId = null;
        }

        if (action.type === "changeSyncInProgress") {
            stateCopy.syncInProgress = action.val;
        }

        if (action.type === "changeCategory") {
            stateCopy.assignmentCategoryMap[action.assignmentId] = action.catId;
            console.log(stateCopy.assignmentCategoryMap);
        }

        return stateCopy;
    }
    const [dataState, dispatch] = useReducer(stateReducer, initState);

    const fetchAdminRole = async () => {
        const token = await authentication.getAuthToken();
        const res = await ApiService.apiGetRequest(token, "/api/is-admin");
        props.setIsAdmin(res.data);
    }
    
    const getAssignmentsWithCategories = async (polledJobFinished = false) => {
        const token = await authentication.getAuthToken();
        const classId = tabContext["team"]["groupId"];
        console.log(`ClassID: ${classId}`);

        const assignmentsResponse = await ApiService.apiGetRequest(token, `/api/get-assignments-by-class/${classId}`);
        let categoryList = null;
        if (defaultConnectionId) {
            categoryList = await getOneRosterCategories(token);
        }

        dispatch({
            type: "setAssignments",
            assignmentList: assignmentsResponse.data,
            categories: categoryList,
            polledJobFinished: polledJobFinished
        });
    }

    const getSisIdMappingState = async () => {
        setFetchingMappingState(true);
        const token = await authentication.getAuthToken();
        const classId = tabContext["team"]["groupId"];

        try {
            const res = await ApiService.apiGetRequest(token, `/api/graph-external-id-mapping-state/${classId}`);
            dispatch({ type: "setMappingToolState", mappingState: res.data });
        } catch (e) { console.log(e) }

        setFetchingMappingState(false);
    }

    const changeMappingToolPage = (page) => {
        dispatch({ type: "changeMappingToolPage", page: page });
    }

    const markMappingPageComplete = (page) => {
        dispatch({ type: "markMappingPageDataTrue", page: page });
    }

    const getOneRosterCategories = async (token) => {
        const res = await ApiService.apiGetRequest(token, `/api/line-item-categories/${defaultConnectionId}`);
        return res.data;
    }

    const onRunSync = async () => {
        dispatch({type: "quickChangeSyncStatus"});

        const token = await authentication.getAuthToken();
        const classId = tabContext["team"]["groupId"];
        
        const idList = [];
        for (const assignment of dataState.assignments) {
            if (assignment.queuedToSync) {
                idList.push(assignment.assignmentId);
            }
        }
        const res = await ApiService
            .apiPostRequest(token, `/api/queue-gradesync/${classId}/${defaultConnectionId}/${forceSync}`,
            { 
                idList: idList,
                categoryMap: dataState.assignmentCategoryMap
            });

        console.log(`JOB-ID started: ${res.data}`);
        setRunningJobId(res.data);
    }

    const onCancelSync = async () => {
        setCancelSyncInProgress(true);

        const token = await authentication.getAuthToken();
        const classId = tabContext["team"]["groupId"];
        await ApiService.apiGetRequest(token, `/api/cancel-gradesync/${classId}/${runningJobId}`);

        setCancelSyncInProgress(false);
        dispatch({ type: "changeSyncInProgress", val: false });
    }

    const getDefaultConnection = async () => {
        await teamsjs.app.initialize();
        const token = await authentication.getAuthToken();

        try {
            const res = await ApiService.apiGetRequest(token, "/api/default-connection-id");
            setDefaultConnectionId(res.data);
        } catch (e) {
            setDefaultConnectionId(null);
            setConnectionWarning(true);
        }
    }

    const pollJobStatus = async () => {
        const token = await authentication.getAuthToken();
        const classId = tabContext["team"]["groupId"];
        const res = await ApiService.apiGetRequest(token, `/api/get-job-status/${classId}/${runningJobId}`);
        setJobStatus(res.data);
    }

    const resetPollState = () => {
        setRunningJobId("");
        setJobStatus(null);
        setJobInterval(null);
    }

    useEffect(() => {
        if (dataState.activeJobId) {
            setRunningJobId(dataState.activeJobId);
        }
    }, [dataState]);

    useEffect(() => {
        if (runningJobId && runningJobId !== "") {
            dispatch({ type: "clearActiveJobState"});
            const interval = setInterval(() => {
                pollJobStatus();
            }, 5000);

            setJobInterval(interval);
            return () => clearInterval(interval);
        }
    }, [runningJobId]);

    useEffect(() => {
        if (jobStatus === JobStatus.Finished || jobStatus === JobStatus.Cancelled) {
            clearInterval(jobInterval);
            resetPollState();
            getAssignmentsWithCategories(true);
        }
    }, [jobStatus]);

    useEffect(() => {
        if (defaultConnectionId !== undefined) {
            getAssignmentsWithCategories();
            fetchAdminRole();
            getSisIdMappingState();
        }
    }, [defaultConnectionId]);

    useEffect(() => {
        if (tabContext) {
            getDefaultConnection();
        }
    }, [tabContext]);

    const onShowErrorsModal = (e, assignment) => {
        e.preventDefault();
        dispatch({ type: "showErrorsModal", assignment: assignment});
    }

    const onCloseErrorsModal = () => {
        dispatch({ type: "hideErrorsModal" });
    }

    const onCloseMappingModal = () => {
        setShowMappingModal(false);
    }

    const onManageConnections = () => {
        navigate("/manage-connections");
    }

    const getErrorsModal = () => {
        return (
            <Modal isOpen={dataState.errorsModal.show} toggle={onCloseErrorsModal} size='lg'>
                <ModalHeader toggle={onCloseErrorsModal}>Sync Errors: {dataState.errorsModal.assignment.displayName}</ModalHeader>
                <ModalBody className="error-modal">
                    {`Reference Id: ${dataState.errorsModal.assignment.currentSyncJobId}` }
                    <br/>
                    <br/>
                    { dataState.errorsModal.assignment.gradeSyncErrorMessage }
                </ModalBody>
                <ModalFooter>
                    <button type="button" className="btn btn-secondary" onClick={onCloseErrorsModal}>Close</button>
                </ModalFooter>
            </Modal>
        );
    }

    const checkInput = (disabled, checked, handler) => {
        return (
            <div className="form-check mb-0">
                <input 
                    className="form-check-input" 
                    type="checkbox"
                    disabled={disabled}
                    checked={checked}
                    onChange={handler}
                /> 
            </div>
        ); 
    }

    const getSyncAllSwitch = () => {
        if (dataState.syncInProgress || !dataState.fetchedAssignments || showNoDefaultConnectionWarning || dataState.showMappingToolWarning) {
            return checkInput(true, false, null);        
        } else {
            if (dataState.syncAll) {
                return checkInput(false, true, () => dispatch({type: "syncAll", value: false}));                
            } else {
                return checkInput(false, false, () => dispatch({type: "syncAll", value: true}));       
            }
        }
    }

    const getAddToSyncSwitch = (assignment) => {
        if (
            dataState.syncInProgress || 
            !dataState.fetchedAssignments || 
            showNoDefaultConnectionWarning || 
            assignment.status === "draft" ||
            !assignment.maxPoints ||
            dataState.showMappingToolWarning) {
            return checkInput(true, false, null);
        } else {
            if (assignment.queuedToSync) {
                return checkInput(false, true, () => dispatch({ type: "dequeueSync", assignment: assignment }));
            } else {
                return checkInput(false, false, () => dispatch({ type: "queueSync", assignment: assignment }));
            }
        }
    }

    const getCategorySelect = (assignment) => {
        if (assignment.status === "draft" || !assignment.maxPoints) return null;

        const catId = dataState.assignmentCategoryMap.hasOwnProperty(assignment.assignmentId) 
            ? dataState.assignmentCategoryMap[assignment.assignmentId]
            : "none";

        if (assignment.canSetCategory) {
            return (
                <select className="form-select" value={catId} onChange={e => dispatch({ type: "changeCategory", assignmentId: assignment.assignmentId, catId: e.target.value })}>
                    <option value="none">None</option>
                    {
                        dataState.oneRosterAssignmentCategories.map((category) => {
                            return (
                                <option value={category.id}>{category.title}</option>
                            );
                        })
                    }
                </select>
            );
        } else {
            return (
                <select className="form-select" value={catId} disabled>
                    <option value="none">None</option>
                    {
                        dataState.oneRosterAssignmentCategories.map((category) => {
                            return (
                                <option value={category.id}>{category.title}</option>
                            );
                        })
                    }
                </select>
            );
        }
    }

    const syncStatusBadge = (assignment) => {
        switch(assignment.syncStatus) {
            case SyncStatus.NotSynced:
                return <span className="badge rounded-pill bg-secondary">Not synced</span>
            case SyncStatus.InProgress:
                return <span className="badge rounded-pill bg-primary">In progress</span>
            case SyncStatus.Synced:
                return <span className="badge rounded-pill bg-success">Synced</span>
            case SyncStatus.Cancelled:
                return <span className="badge rounded-pill bg-warning text-dark">Cancelled</span>
            case SyncStatus.Failed:
                return (
                    <Fragment>
                        <span className="badge rounded-pill bg-danger">Failed</span>
                        <span className="ms-3"><a href="#" onClick={e => onShowErrorsModal(e, assignment)}>Errors</a></span> 
                    </Fragment>
                );    
        }
    }

    const toLocalTime = (timestamp) => {
        const date = new Date(timestamp);
        return date.toLocaleString();
    }

    const getAssignmentsTableRows = () => {
        if (dataState.fetchedAssignments) {
            const hasCategories = dataState.oneRosterAssignmentCategories.length > 0 ? true : false;
            return (
                dataState.assignments.map((assignment) => {
                        if (assignment.status === "draft") return null; // we don't want to show draft assignments

                        return (
                            <tr>
                                <td className="border-start-0">{ getAddToSyncSwitch(assignment) }</td>
                                <td>{ assignment.displayName }</td>
                                <td>{ assignment.dueTimestamp ? toLocalTime(assignment.dueTimestamp) : "-" }</td>
                                <td>{ hasCategories ? getCategorySelect(assignment) : "-" }</td>
                                <td>{ assignment.lastSyncTimestamp ? toLocalTime(assignment.lastSyncTimestamp) : "-" }</td>
                                <td className="border-end-0">{ syncStatusBadge(assignment) }</td>  
                            </tr>
                        );
                    })
            );
        } else {
            const spinner = 
                <div className="spinner-grow spinner-grow-sm text-secondary">
                    <span className="visually-hidden">Loading...</span>
                </div>
            return (
                <tr>
                    <td className="border-start-0">{ getAddToSyncSwitch(null) }</td>
                    <td>{spinner}</td>
                    <td>{spinner}</td>
                    <td>{spinner}</td>
                    <td>{spinner}</td>
                    <td className="border-start-0">{spinner}</td>
                </tr>
            );
        }
    }

    const getAssignmentList = () => {
        return(
            <table className="table table-striped table-bordered align-middle">
                <thead>
                    <tr>
                        <th className="custom-header-border border-start-0" scope="col">{ getSyncAllSwitch() }</th>
                        <th className="custom-header-border" scope="col">Assignment</th>
                        <th className="custom-header-border" scope="col">Due Date</th>
                        <th className="custom-header-border" scope="col">SIS Category</th>
                        <th className="custom-header-border" scope="col">Last Synced Time</th>
                        <th className="custom-header-border border-end-0" scope="col">Sync Status</th>
                    </tr>
                </thead>
                <tbody>
                    { getAssignmentsTableRows() }
                </tbody>
            </table>   
        );
    }

    const syncingSpinner = () => {
        if (dataState.syncInProgress) {
            const spinnerClass = cancelSyncInProgress ? "spinner-border text-warning" : "spinner-border text-primary";
            return(
                <span className="float-end my-auto me-3">
                    <div className={spinnerClass}>
                        <span className="visually-hidden">Syncing</span>
                    </div>
                </span>
            );
        } else return null;
    }

    const noConnectionWarning = () => {
        if (showNoDefaultConnectionWarning) {
            return (
                <div className="alert alert-warning d-flex align-items-center position-fixed bottom-0 end-0 mb-3 me-3">
                    <svg xmlns="http://www.w3.org/2000/svg" width="22" height="22" fill="currentColor" class="bi bi-exclamation-triangle-fill flex-shrink-0 me-2" viewBox="0 0 16 16" role="img" aria-label="Warning:">
                        <path d="M8.982 1.566a1.13 1.13 0 0 0-1.96 0L.165 13.233c-.457.778.091 1.767.98 1.767h13.713c.889 0 1.438-.99.98-1.767L8.982 1.566zM8 5c.535 0 .954.462.9.995l-.35 3.507a.552.552 0 0 1-1.1 0L7.1 5.995A.905.905 0 0 1 8 5zm.002 6a1 1 0 1 1 0 2 1 1 0 0 1 0-2z"/>
                    </svg>
                    <div>
                        No default API connection! Go to <strong>Manage API Connections</strong> and select one.
                    </div>
                </div>
            );
        } else return null;
    }

    const mappingToolWarning = () => {
        // never show the tool launch UI for anyone if the feature is disabled
        if (dataState.mappingToolDisabled) return null;

        if (dataState.showMappingToolWarning || props.isAdmin) {
            return (
                <div className="alert alert-warning d-flex align-items-center mb-3">
                    <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" fill="currentColor" class="bi flex-shrink-0 me-2" viewBox="0 0 16 16" role="img" aria-label="Warning:">
                        <path d="M8 16A8 8 0 1 0 8 0a8 8 0 0 0 0 16zm.93-9.412-1 4.705c-.07.34.029.533.304.533.194 0 .487-.07.686-.246l-.088.416c-.287.346-.92.598-1.465.598-.703 0-1.002-.422-.808-1.319l.738-3.468c.064-.293.006-.399-.287-.47l-.451-.081.082-.381 2.29-.287zM8 5.5a1 1 0 1 1 0-2 1 1 0 0 1 0 2z"/>
                    </svg>
                    <div>
                        Please link your Teams Class to your OneRoster classes/students.
                    </div>
                    {
                        !defaultConnectionId || fetchingMappingState ?
                            <button className="btn btn-warning ms-auto" type="button" disabled>
                                Create link
                            </button> :
                            <button className="btn btn-warning ms-auto" type="button" onClick={() => setShowMappingModal(true)}>
                                Create link
                            </button>
                    }
                </div>
            );
        } else return null;
    }

    const forceSyncOption = () => {
        if (
            dataState.syncInProgress ||
            showNoDefaultConnectionWarning ||
            dataState.showMappingToolWarning
        ) { 
            return (
                <div className="form-check form-switch my-auto">
                    <label className="form-check-label" for="force-sync">Force-sync</label>
                    <input 
                        className="form-check-input"
                        id="force-sync"
                        type="checkbox" 
                        checked={forceSync}
                        disabled
                    />
                </div>
            );
        } else {
            return (
                <div className="form-check form-switch my-auto">
                    <label className="form-check-label" for="force-sync">Force-sync</label>
                    <input 
                        className="form-check-input"
                        id="force-sync"
                        type="checkbox" 
                        checked={forceSync}
                        onChange={() => setForceSync(!forceSync)}
                    />   
                </div>
            );
        }
    }

    const syncOrCancelButton = () => {
        if (dataState.syncInProgress && runningJobId !== "") {
            return <button type="button" className="btn btn-warning" onClick={onCancelSync}>Cancel</button>;
        } else if (
            dataState.syncInProgress || 
            dataState.assignmentSelectedCount === 0 || 
            showNoDefaultConnectionWarning || 
            dataState.showMappingToolWarning
        ) {
            return <button type="button" className="btn btn-primary" disabled>Run sync</button>;
        } else {
            return <button type="button" className="btn btn-primary" onClick={onRunSync}>Run sync</button>;
        }
    }

    if (!props.eduPrimaryRole) {
        return null;
    } else if (props.eduPrimaryRole === "student") {
        return (
            <div className="row justify-content-center">
                <div className="col-8 mt-4 text-center">
                    <h4 className="mb-4 ms-auto">To view your grades for this class, click the <strong>Grades</strong> tab on the left</h4>
                    <img src={noTeams} style={{ width: "100%", height: "auto" }} /> 
                </div>
            </div>
        );
    } else {
        return (
            <Fragment>
                <div className="row justify-content-center">
                    <div className="col-xl-10 col-11">

                        <div className="d-flex flex-row mb-3">
                            <div className="d-flex">
                                <h3>Assignments</h3>
                            </div>

                            <div className="d-flex ms-auto">
                                { syncingSpinner() }

                                <span className="float-end my-auto me-2">
                                    <button type="button" className="btn btn-outline-primary" onClick={onManageConnections}>Manage API Connections</button>
                                </span>

                                <span className="float-end my-auto">
                                    { syncOrCancelButton() }
                                </span>
                            </div>
                        </div>

                        <div className="d-flex flex-row mb-5">
                            <div className="d-flex ms-auto">
                                <span className="float-end my-auto">
                                    { forceSyncOption() }
                                </span>
                            </div>
                        </div>

                        { mappingToolWarning() }

                        <div className="container-fluid p-0">
                            { getAssignmentList() }
                        </div>

                    </div>
                </div>

                { noConnectionWarning() }
                { getErrorsModal() }

                { 
                    <MappingModal 
                        isOpen={showMappingModal} 
                        toggle={onCloseMappingModal}
                        dataState={dataState}
                        isAdmin={props.isAdmin}
                        defaultConnectionId={defaultConnectionId}
                        changePage={(page) => changeMappingToolPage(page)}
                        markPageComplete={(page) => markMappingPageComplete(page)}
                        onModalClose={getSisIdMappingState}
                    />
                }
            </Fragment>
        );
    }
}

export default Home;
